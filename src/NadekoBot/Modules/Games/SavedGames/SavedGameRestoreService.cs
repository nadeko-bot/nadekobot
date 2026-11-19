using NadekoBot.Common.ModuleBehaviors;

namespace NadekoBot.Modules.Games.SavedGames;

public enum SavedGameRestoreResult : byte
{
    Restored,
    Drop,

    // a problem which can pass, for example a server in a Discord outage or a Discord 5xx
    Retry
}

public interface ISavedGameHandler
{
    SavedGameKind Kind { get; }

    // an exception counts as Retry
    Task<SavedGameRestoreResult> RestoreAsync(SavedGame game);

    // deletes the game and gives back what it took from members
    Task DropAsync(SavedGame game);
}

// Continues the games of this shard which a restart interrupted.
public sealed class SavedGameRestoreService(
    SavedGameStore store,
    ShardData shardData,
    IEnumerable<ISavedGameHandler> handlers,
    TimeProvider? timeProvider = null) : INService, IReadyExecutor
{
    // an older game was abandoned, or the bot was down so long that nobody waits for it
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(15);

    // one post at a time, so a restart does not send a burst of messages
    public static readonly TimeSpan RestoreGap = TimeSpan.FromMilliseconds(250);

    // a game which can come back is tried again after this delay, until the age rule drops it
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    private const int LOAD_ATTEMPTS = 5;
    private static readonly TimeSpan _loadRetryDelay = TimeSpan.FromSeconds(5);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly ISavedGameHandler[] _handlers = handlers.ToArray();
    private int _started;

    public Task OnReadyAsync()
    {
        if (Interlocked.Exchange(ref _started, 1) == 0)
            _ = Task.Run(RunInternalAsync);

        return Task.CompletedTask;
    }

    private async Task RunInternalAsync()
    {
        for (var attempt = 1; attempt <= LOAD_ATTEMPTS; attempt++)
        {
            SavedGame[] games;
            try
            {
                games = await store.LoadAsync(shardData.TotalShards, shardData.ShardId);
            }
            catch (Exception ex) when (attempt < LOAD_ATTEMPTS)
            {
                Log.Warning(ex, "Could not load the games which a restart interrupted, try {Attempt}", attempt);
                await Task.Delay(_loadRetryDelay, _time);
                continue;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Could not load the games which a restart interrupted. The next restart tries again");
                return;
            }

            if (games.Length == 0)
                return;

            var restored = await RestoreAsync(games);
            Log.Information("Continued {Restored} of {Count} games which a restart interrupted", restored, games.Length);
            return;
        }
    }

    public async Task<int> RestoreAsync(IReadOnlyList<SavedGame> games)
    {
        var retry = new List<SavedGame>();
        var restored = await RestorePassInternalAsync(games, retry);

        while (retry.Count > 0)
        {
            await Task.Delay(RetryDelay, _time);

            var again = retry.ToArray();
            retry.Clear();
            restored += await RestorePassInternalAsync(again, retry);
        }

        return restored;
    }

    private async Task<int> RestorePassInternalAsync(IReadOnlyList<SavedGame> games, List<SavedGame> retry)
    {
        var oldest = (_time.GetUtcNow() - MaxAge).UtcDateTime;
        var restored = 0;
        var posted = false;

        foreach (var game in games)
        {
            if (FindHandlerInternal(game.Kind) is not { } handler)
            {
                Log.Warning("No handler continues saved games of kind {Kind}. The bot deletes the game", game.Kind);
                await store.EndOrRetryAsync(game.Id);
                continue;
            }

            // this also ends the retries of a game which never comes back
            if (game.DateAdded < oldest)
            {
                await DropInternalAsync(handler, game);
                continue;
            }

            if (posted)
                await Task.Delay(RestoreGap, _time);

            posted = true;

            SavedGameRestoreResult result;
            try
            {
                result = await handler.RestoreAsync(game);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not continue the {Kind} game in channel {ChannelId}. The bot tries again", game.Kind, game.ChannelId);
                result = SavedGameRestoreResult.Retry;
            }

            switch (result)
            {
                case SavedGameRestoreResult.Restored:
                    restored++;
                    break;
                case SavedGameRestoreResult.Retry:
                    retry.Add(game);
                    break;
                default:
                    await DropInternalAsync(handler, game);
                    break;
            }
        }

        return restored;
    }

    private ISavedGameHandler? FindHandlerInternal(SavedGameKind kind)
    {
        foreach (var handler in _handlers)
        {
            if (handler.Kind == kind)
                return handler;
        }

        return null;
    }

    private async Task DropInternalAsync(ISavedGameHandler handler, SavedGame game)
    {
        try
        {
            await handler.DropAsync(game);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not drop the {Kind} game in channel {ChannelId}. The bot tries again", game.Kind, game.ChannelId);
            store.RetryLater(game.Id, () => handler.DropAsync(game));
        }
    }
}
