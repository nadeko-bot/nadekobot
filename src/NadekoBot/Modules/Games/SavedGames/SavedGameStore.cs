using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Services.Currency;

namespace NadekoBot.Modules.Games.SavedGames;

public enum SavedGameAddResult : byte
{
    Added,
    AlreadyRunning,
    LimitReached
}

public readonly record struct SavedGameAdd(SavedGameAddResult Result, int Id);

// A start and an end write at once. The state of a running game waits in memory, and each shard writes
// all changed games in one transaction, so a game which changes often costs no extra database write.
public sealed class SavedGameStore(DbService db, ITxTracker txTracker, TimeProvider? timeProvider = null)
    : INService, IReadyExecutor
{
    public static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(5);

    // keeps one write transaction short also when many games changed
    public const int MAX_UPDATES_PER_FLUSH = 500;

    // a failed end waits 2^n intervals before its next try, so at most about 5 minutes
    private const int MAX_RETRY_BACKOFF_SHIFT = 6;

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    // the newest state which is not in the database yet, by row id
    private readonly ConcurrentDictionary<int, byte[]> _pending = new();

    // one state write at a time, so an older state in the batch cannot overwrite a newer SaveNowAsync
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    // ends which failed, by row id; until one works, its row stops a new game in the same place
    private readonly ConcurrentDictionary<int, EndRetry> _retries = new();
    private int _started;

    public Task OnReadyAsync()
    {
        if (Interlocked.Exchange(ref _started, 1) == 0)
        {
            _ = Task.Run(FlushLoopInternalAsync);
            _ = Task.Run(RetryLoopInternalAsync);
        }

        return Task.CompletedTask;
    }

    private async Task FlushLoopInternalAsync()
    {
        using var timer = new PeriodicTimer(FlushInterval, _time);
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                await FlushAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "The bot could not save the state of running games. It tries again with the next batch");
            }
        }
    }

    // a separate loop, so an end which waits for a busy database does not delay the batch
    private async Task RetryLoopInternalAsync()
    {
        using var timer = new PeriodicTimer(FlushInterval, _time);
        while (await timer.WaitForNextTickAsync())
            await RetryEndsInternalAsync();
    }

    // The end must be safe to run again: it deletes the row and moves currency in one transaction.
    // The first end of a row stays, because it is the one which carries the reward.
    public void RetryLater(int id, Func<Task> end)
        => _retries.TryAdd(id, new(end));

    private async Task RetryEndsInternalAsync()
    {
        var now = _time.GetUtcNow();
        foreach (var (id, retry) in _retries)
        {
            if (now < retry.NextTry)
                continue;

            try
            {
                await retry.End();
                _retries.TryRemove(id, out _);
            }
            catch (Exception ex)
            {
                retry.Attempts++;
                retry.NextTry = now + (FlushInterval * (1 << Math.Min(retry.Attempts, MAX_RETRY_BACKOFF_SHIFT)));
                Log.Warning(ex,
                    "The bot could not end the saved game {Id}, try {Attempt}. It tries again later",
                    id,
                    retry.Attempts);
            }
        }
    }

    public async Task<SavedGameAdd> AddAsync(
        ulong guildId,
        ulong channelId,
        SavedGameKind kind,
        int perServerLimit,
        byte[] state)
    {
        var now = _time.GetUtcNow().UtcDateTime;

        await using var ctx = db.GetDbContext();
        await using var lctx = ctx.CreateLinqToDBContext();
        var games = lctx.GetTable<SavedGame>();

        // The checks and the insert are one statement, so parallel commands cannot pass the limit.
        var ids = await lctx.SelectQuery(() => 1)
            .Where(_ => !games.Any(g => g.ChannelId == channelId && g.Kind == kind)
                        && games.Count(g => g.GuildId == guildId && g.Kind == kind) < perServerLimit)
            .InsertWithOutputAsync(games,
                _ => new SavedGame
                {
                    GuildId = guildId,
                    ChannelId = channelId,
                    Kind = kind,
                    State = state,
                    DateAdded = now
                },
                g => g.Id);

        if (ids.Length > 0)
            return new(SavedGameAddResult.Added, ids[0]);

        var running = await games.AnyAsyncLinqToDB(g => g.ChannelId == channelId && g.Kind == kind);
        return new(running ? SavedGameAddResult.AlreadyRunning : SavedGameAddResult.LimitReached, 0);
    }

    public void Update(int id, byte[] state)
        => _pending[id] = state;

    public async Task<int> FlushAsync()
    {
        if (_pending.IsEmpty)
            return 0;

        await _writeLock.WaitAsync();
        try
        {
            var batch = new List<KeyValuePair<int, byte[]>>(Math.Min(_pending.Count, MAX_UPDATES_PER_FLUSH));
            foreach (var (id, _) in _pending)
            {
                if (batch.Count == MAX_UPDATES_PER_FLUSH)
                    break;

                if (_pending.TryRemove(id, out var state))
                    batch.Add(new(id, state));
            }

            if (batch.Count == 0)
                return 0;

            await WriteInternalAsync(batch);
            return batch.Count;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // for a change which must not wait for the next batch; a newer state which came meanwhile wins
    public async Task SaveNowAsync(int id, byte[] state)
    {
        _pending[id] = state;

        await _writeLock.WaitAsync();
        try
        {
            // the batch already wrote it
            if (!_pending.TryRemove(id, out var newest))
                return;

            await WriteInternalAsync([new(id, newest)]);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task WriteInternalAsync(List<KeyValuePair<int, byte[]>> states)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        try
        {
            await using var ctx = db.GetDbContext();
            await using var tx = await ctx.Database.BeginTransactionAsync();

            // An update never inserts, so a late state cannot bring back a game which ended.
            foreach (var (id, state) in states)
            {
                await ctx.GetTable<SavedGame>()
                    .Where(x => x.Id == id)
                    .Set(x => x.State, state)
                    .Set(x => x.DateAdded, now)
                    .UpdateAsync();
            }

            await tx.CommitAsync();
        }
        catch
        {
            // a state which came during the failed write is newer, so it stays
            foreach (var (id, state) in states)
                _pending.TryAdd(id, state);

            throw;
        }
    }

    // The row and the reward change in one transaction, so only the call which deletes the row pays.
    public async Task<bool> EndAsync(int id, ulong winnerId = 0, long reward = 0, TxData? txData = null)
    {
        _pending.TryRemove(id, out _);
        var pays = reward > 0 && txData is not null;

        await using (var ctx = db.GetDbContext())
        {
            await using var tx = await ctx.Database.BeginTransactionAsync();

            var deleted = await ctx.GetTable<SavedGame>()
                .Where(x => x.Id == id)
                .DeleteAsync();

            if (deleted == 0)
                return false;

            if (pays)
                await DefaultWallet.AddAsync(ctx, winnerId, reward, txData);

            await tx.CommitAsync();
        }

        if (pays)
        {
            try
            {
                await txTracker.TrackAdd(winnerId, reward, txData);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not track a game reward of {Amount} to {UserId}", reward, winnerId);
            }
        }

        return true;
    }

    // for a caller which has nothing to do when the end fails
    public async Task EndOrRetryAsync(int id, ulong winnerId = 0, long reward = 0, TxData? txData = null)
    {
        try
        {
            await EndAsync(id, winnerId, reward, txData);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "The bot could not end the saved game {Id}. It tries again later", id);
            RetryLater(id, () => EndAsync(id, winnerId, reward, txData));
        }
    }

    public async Task<SavedGame[]> LoadAsync(int totalShards, int shardId)
    {
        await using var ctx = db.GetDbContext();
        return await ctx.GetTable<SavedGame>()
            .Where(Queries.GuildOnShard<SavedGame>(x => x.GuildId, totalShards, shardId))
            .OrderBy(x => x.Id)
            .ToArrayAsyncLinqToDB();
    }

    // only the retry loop changes an entry after it is added
    private sealed class EndRetry(Func<Task> end)
    {
        public Func<Task> End { get; } = end;
        public int Attempts { get; set; }
        public DateTimeOffset NextTry { get; set; }
    }
}
