using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.Configs;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Owner.Services;

public sealed class BotActivityService(
    IPubSub pubSub,
    DiscordSocketClient client,
    DbService db,
    IReplacementService rep,
    BotConfigService bss) : IBotActivityService, IReadyExecutor, INService
{
    private static readonly TypedKey<PresencePubData> _presenceKey = new("presence.apply");
    private static readonly TimeSpan _rotateInterval = TimeSpan.FromMinutes(1);

    // Placeholder replacement awaits, so an older activity change could finish after a newer one.
    private long _activityVersion;

    public async Task<string?> RemovePlayingAsync(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        await using var ctx = db.GetDbContext();
        var toRemove = await ctx.GetTable<RotatingPlayingStatus>()
                                .OrderBy(x => x.Id)
                                .Skip(index)
                                .FirstOrDefaultAsyncLinqToDB();

        if (toRemove is null)
            return null;

        await ctx.GetTable<RotatingPlayingStatus>()
                 .Where(x => x.Id == toRemove.Id)
                 .DeleteAsync();

        return toRemove.Status;
    }

    public async Task AddPlaying(ActivityType activityType, string status)
    {
        await using var ctx = db.GetDbContext();
        await ctx.GetTable<RotatingPlayingStatus>()
                 .InsertAsync(() => new()
                 {
                     Status = status,
                     Type = (DbActivityType)activityType
                 });
    }

    public async Task<bool> ToggleRotatePlayingAsync()
    {
        var enabled = false;
        bss.ModifyConfig(bs => { enabled = bs.RotateStatuses = !bs.RotateStatuses; });

        // The last rotation entry would stay until a restart.
        if (!enabled)
            await PublishSavedInternalAsync();

        return enabled;
    }

    public IReadOnlyList<RotatingPlayingStatus> GetRotatingStatuses()
    {
        using var ctx = db.GetDbContext();
        return ctx.GetTable<RotatingPlayingStatus>().OrderBy(x => x.Id).ToList();
    }

    public BotPresenceState GetPresence()
    {
        var data = bss.Data;
        var (status, activity) = BotPresence.Normalize(data.Presence);
        return new(status, activity, data.RotateStatuses);
    }

    public async Task<PresenceError> SetPresenceAsync(UserStatus status, ActivityType? type, string? text, string? url)
    {
        if (!BotPresence.IsSettableStatus(status))
            return PresenceError.Status;

        PresenceActivity? activity = null;
        if (type is { } t)
        {
            var error = BotPresence.TryCreateActivity(t, text, url, out activity);
            if (error != PresenceError.None)
                return error;
        }

        await SaveActivityInternalAsync(status, activity);
        return PresenceError.None;
    }

    public async Task<PresenceError> SetStatusAsync(UserStatus status)
    {
        if (!BotPresence.IsSettableStatus(status))
            return PresenceError.Status;

        bss.ModifyConfig(c =>
        {
            c.Presence ??= new();
            c.Presence.Status = status;
        });

        // The activity stays as it is, so a running rotation keeps going.
        await pubSub.Pub(_presenceKey, new() { Status = status });
        return PresenceError.None;
    }

    public async Task<PresenceError> SetActivityAsync(ActivityType? type, string? text)
    {
        PresenceActivity? activity = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            var error = BotPresence.TryCreateActivity(type ?? ActivityType.CustomStatus, text, null, out activity);
            if (error != PresenceError.None)
                return error;
        }

        await SaveActivityInternalAsync(null, activity);
        return PresenceError.None;
    }

    public async Task<PresenceError> SetStreamAsync(string url, string name)
    {
        var error = BotPresence.TryCreateActivity(ActivityType.Streaming, name, url, out var activity);
        if (error != PresenceError.None)
            return error;

        await SaveActivityInternalAsync(null, activity);
        return PresenceError.None;
    }

    public Task ApplySavedAsync()
        => ApplyInternalAsync(SavedPubDataInternal()).AsTask();

    private Task SaveActivityInternalAsync(UserStatus? status, PresenceActivity? activity)
    {
        bss.ModifyConfig(c =>
        {
            c.Presence ??= new();
            if (status is { } s)
                c.Presence.Status = s;

            c.Presence.Activity = activity;
            c.RotateStatuses = false;
        });

        return PublishSavedInternalAsync();
    }

    private Task PublishSavedInternalAsync()
        => pubSub.Pub(_presenceKey, SavedPubDataInternal());

    private PresencePubData SavedPubDataInternal()
    {
        var (status, activity) = BotPresence.Normalize(bss.Data.Presence);
        return new()
        {
            Status = status,
            SetActivity = true,
            Type = activity?.Type ?? ActivityType.Playing,
            Text = activity?.Text,
            Url = activity?.Url
        };
    }

    private async ValueTask ApplyInternalAsync(PresencePubData data)
    {
        // A rotation tick which was sent before the rotation stopped must not replace the saved activity.
        if (data.FromRotation && !bss.Data.RotateStatuses)
            return;

        var version = data.FromRotation || !data.SetActivity
            ? Volatile.Read(ref _activityVersion)
            : Interlocked.Increment(ref _activityVersion);

        try
        {
            if (data.Status is { } status && client.Status != status)
                await client.SetStatusAsync(status);

            if (!data.SetActivity)
                return;

            var text = data.Text is null
                ? null
                : await rep.ReplaceAsync(data.Text, new(client: client));

            // A placeholder can make the text longer; the cut must not split a surrogate pair.
            if (text is { Length: > BotPresence.MAX_TEXT_LENGTH })
            {
                text = text[..(char.IsHighSurrogate(text[BotPresence.MAX_TEXT_LENGTH - 1])
                    ? BotPresence.MAX_TEXT_LENGTH - 1
                    : BotPresence.MAX_TEXT_LENGTH)];
            }

            if (version != Volatile.Read(ref _activityVersion) || (data.FromRotation && !bss.Data.RotateStatuses))
                return;

            if (IsShownInternal(client.Activity, data.Type, text, data.Url))
                return;

            await client.SetGameAsync(text, data.Url, data.Type);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Unable to set the bot's presence");
        }
    }

    private static bool IsShownInternal(IActivity? current, ActivityType type, string? text, string? url)
    {
        if (string.IsNullOrEmpty(text))
            return current is null;

        if (current is null || current.Type != type)
            return false;

        return current switch
        {
            CustomStatusGame custom => custom.State == text,
            StreamingGame stream => stream.Name == text && stream.Url == url,
            _ => current.Name == text
        };
    }

    public async Task OnReadyAsync()
    {
        await pubSub.Sub(_presenceKey, ApplyInternalAsync);

        // Discord.Net sends the presence again in each identify, so a reconnect keeps it.
        await ApplySavedAsync();

        if (client.ShardId == 0)
            _ = Task.Run(RotateLoopInternalAsync);
    }

    private async Task RotateLoopInternalAsync()
    {
        using var timer = new PeriodicTimer(_rotateInterval);
        var index = 0;
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                if (!bss.Data.RotateStatuses)
                    continue;

                List<RotatingPlayingStatus> statuses;
                await using (var ctx = db.GetDbContext())
                {
                    statuses = await ctx.GetTable<RotatingPlayingStatus>()
                                        .OrderBy(x => x.Id)
                                        .ToListAsyncLinqToDB();
                }

                if (statuses.Count == 0)
                    continue;

                if (index >= statuses.Count)
                    index = 0;

                var next = statuses[index++];

                // Each shard fills in the placeholders, so the text goes out as the owner wrote it.
                await pubSub.Pub(_presenceKey,
                    new()
                    {
                        SetActivity = true,
                        Type = (ActivityType)next.Type,
                        Text = next.Status,
                        FromRotation = true
                    });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Rotating playing status errored: {ErrorMessage}", ex.Message);
            }
        }
    }

    public sealed class PresencePubData
    {
        public UserStatus? Status { get; init; }
        public bool SetActivity { get; init; }
        public ActivityType Type { get; init; }
        public string? Text { get; init; }
        public string? Url { get; init; }
        public bool FromRotation { get; init; }
    }
}
