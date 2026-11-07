using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using System.Net;
using System.Threading.Channels;

namespace NadekoBot.Modules.Administration.Services;

public sealed class AutoThreadService(
    DbService db,
    DiscordSocketClient client,
    ShardData shardData) : IExecNoCommand, IReadyExecutor, INService
{
    public const int MAX_THREAD_NAME_LENGTH = 40;
    public const int MAX_BACKFILL = 10;

    private const int BACKFILL_DELAY_MS = 3000;
    private const int BACKFILL_FETCH_MULTIPLIER = 10;
    private const int MAX_BACKFILL_FETCH = 100;
    private const int BOT_QUEUE_CAPACITY = 256;
    private const int BOT_QUEUE_WORKERS = 2;
    private const long DROP_WARNING_INTERVAL_MS = 60_000;

    private ConcurrentDictionary<ulong, AutoThreadSetting> _channels = new();

    private readonly Channel<IUserMessage> _botQueue = Channel.CreateBounded<IUserMessage>(
        new BoundedChannelOptions(BOT_QUEUE_CAPACITY)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });

    private long _lastDropWarning = -DROP_WARNING_INTERVAL_MS;
    private int _droppedSinceWarning;

    public async Task OnReadyAsync()
    {
        await using var uow = db.GetDbContext();
        var items = await uow.GetTable<AutoThreadChannel>()
                             .Where(Queries.GuildOnShard<AutoThreadChannel>(x => x.GuildId,
                                 shardData.TotalShards,
                                 shardData.ShardId))
                             .ToListAsyncLinqToDB();

        var dict = new ConcurrentDictionary<ulong, AutoThreadSetting>();
        foreach (var item in items)
            dict[item.ChannelId] = new(item.GuildId,
                item.Mode,
                ToArchiveDuration(item.ArchiveDurationMinutes),
                item.IncludeBots);

        _channels = dict;

        for (var i = 0; i < BOT_QUEUE_WORKERS; i++)
            _ = Task.Run(BotQueueWorkerInternalAsync);

        client.LeftGuild += OnLeftGuildAsync;
        client.ChannelDestroyed += OnChannelDestroyedAsync;
        client.MessageReceived += OnMessageReceivedAsync;
    }

    // the command handler drops other bots (ignoreOtherBots) and always drops the bot's own messages
    private Task OnMessageReceivedAsync(SocketMessage msg)
    {
        if (!msg.Author.IsBot && !msg.Author.IsWebhook)
            return Task.CompletedTask;

        if (msg is not SocketUserMessage usrMsg || !ShouldThread(usrMsg, fromBot: true))
            return Task.CompletedTask;

        if (!_botQueue.Writer.TryWrite(usrMsg))
            WarnDroppedInternal(usrMsg.Channel.Id);

        return Task.CompletedTask;
    }

    private void WarnDroppedInternal(ulong channelId)
    {
        Interlocked.Increment(ref _droppedSinceWarning);

        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastDropWarning);
        if (now - last < DROP_WARNING_INTERVAL_MS
            || Interlocked.CompareExchange(ref _lastDropWarning, now, last) != last)
            return;

        var dropped = Interlocked.Exchange(ref _droppedSinceWarning, 0);
        Log.Warning("Auto-thread bot message queue is full, dropped {Count} messages, last in channel {ChannelId}",
            dropped,
            channelId);
    }

    private async Task BotQueueWorkerInternalAsync()
    {
        await foreach (var msg in _botQueue.Reader.ReadAllAsync())
        {
            try
            {
                if (ShouldThread(msg, fromBot: true))
                    await CreateThreadInternalAsync(msg);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Auto-thread worker failed for message {MessageId}", msg.Id);
            }
        }
    }

    private bool ShouldThread(IUserMessage msg, bool fromBot)
    {
        if (msg.Channel is not ITextChannel tch || msg.Channel is IThreadChannel)
            return false;

        if (!_channels.TryGetValue(tch.Id, out var setting))
            return false;

        if (fromBot && !setting.IncludeBots)
            return false;

        return setting.Mode != AutoThreadMode.Media || HasMedia(msg);
    }

    private Task OnLeftGuildAsync(SocketGuild guild)
    {
        var guildId = guild.Id;
        foreach (var (channelId, setting) in _channels)
        {
            if (setting.GuildId == guildId)
                _channels.TryRemove(channelId, out _);
        }

        _ = Task.Run(() => DeleteGuildSettingsInternalAsync(guildId));
        return Task.CompletedTask;
    }

    private async Task DeleteGuildSettingsInternalAsync(ulong guildId)
    {
        try
        {
            await using var uow = db.GetDbContext();
            await uow.GetTable<AutoThreadChannel>()
                     .Where(x => x.GuildId == guildId)
                     .DeleteAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Unable to remove the auto thread settings of the left server {GuildId}", guildId);
        }
    }

    private Task OnChannelDestroyedAsync(SocketChannel channel)
    {
        var channelId = channel.Id;
        if (!_channels.TryRemove(channelId, out _))
            return Task.CompletedTask;

        _ = Task.Run(() => DeleteSettingInternalAsync(channelId));
        return Task.CompletedTask;
    }

    private async Task DeleteSettingInternalAsync(ulong channelId)
    {
        try
        {
            await using var uow = db.GetDbContext();
            await uow.GetTable<AutoThreadChannel>()
                     .Where(x => x.ChannelId == channelId)
                     .DeleteAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Unable to remove the auto thread setting of the deleted channel {ChannelId}", channelId);
        }
    }

    public async ValueTask ExecOnNoCommandAsync(IGuild? guild, IUserMessage msg)
    {
        if (guild is null)
            return;

        if (msg.Author.IsBot || msg.Author.IsWebhook)
            return;

        if (!ShouldThread(msg, fromBot: false))
            return;

        await CreateThreadInternalAsync(msg);
    }

    private async Task CreateThreadInternalAsync(IUserMessage msg)
    {
        // the setting may have changed while the message waited
        if (msg.Channel is not ITextChannel tch || !_channels.TryGetValue(tch.Id, out var setting))
            return;

        try
        {
            await tch.CreateThreadAsync(GetThreadName(msg),
                autoArchiveDuration: setting.ArchiveDuration,
                message: msg,
                options: new()
                {
                    RetryMode = RetryMode.AlwaysFail
                });
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
        {
            Log.Warning("Auto-thread disabled in channel {ChannelId} because the bot can't create threads there",
                tch.Id);

            try
            {
                await DisableAsync(tch.GuildId, tch.Id);
            }
            catch (Exception disableEx)
            {
                Log.Warning(disableEx, "Unable to turn off auto-thread in channel {ChannelId}", tch.Id);
            }
        }
        catch (RateLimitedException)
        {
            Log.Debug("Auto-thread was rate limited in channel {ChannelId}", tch.Id);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to create an automatic thread in channel {ChannelId}", tch.Id);
        }
    }

    public async Task<int> BackfillAsync(
        ITextChannel channel,
        AutoThreadMode mode,
        int archiveMinutes,
        bool includeBots,
        int count,
        ulong beforeMessageId)
    {
        var archive = ToArchiveDuration(archiveMinutes);
        var fetchLimit = Math.Min(count * BACKFILL_FETCH_MULTIPLIER, MAX_BACKFILL_FETCH);
        var messages = await channel.GetMessagesAsync(beforeMessageId, Direction.Before, fetchLimit)
                                    .FlattenAsync();
        var targets = SelectBackfillTargets(messages, mode, includeBots, count);

        var created = 0;
        for (var i = 0; i < targets.Count; i++)
        {
            if (i > 0)
                await Task.Delay(BACKFILL_DELAY_MS);

            try
            {
                await channel.CreateThreadAsync(GetThreadName(targets[i]),
                    autoArchiveDuration: archive,
                    message: targets[i],
                    options: new()
                    {
                        RetryMode = RetryMode.AlwaysFail
                    });

                created++;
            }
            catch (Exception ex)
            {
                Log.Warning(ex,
                    "Failed to create a backfill thread for message {MessageId} in channel {ChannelId}",
                    targets[i].Id,
                    channel.Id);
            }
        }

        return created;
    }

    public static List<IUserMessage> SelectBackfillTargets(
        IEnumerable<IMessage> messages,
        AutoThreadMode mode,
        bool includeBots,
        int count)
    {
        var targets = new List<IUserMessage>();

        foreach (var msg in messages)
        {
            if (msg is not IUserMessage userMsg)
                continue;

            if (!includeBots && (msg.Author.IsBot || msg.Author.IsWebhook))
                continue;

            if (msg.Flags.GetValueOrDefault().HasFlag(MessageFlags.HasThread))
                continue;

            if (mode == AutoThreadMode.Media && !HasMedia(userMsg))
                continue;

            targets.Add(userMsg);

            if (targets.Count == count)
                break;
        }

        targets.Sort(static (a, b) => a.Id.CompareTo(b.Id));

        return targets;
    }

    public static bool HasMedia(IUserMessage msg)
        => msg.Attachments.Count > 0 || msg.Embeds.Count > 0 || ContainsLink(msg.Content);

    private static bool ContainsLink(ReadOnlySpan<char> content)
    {
        while (!content.IsEmpty)
        {
            var idx = content.IndexOf("http", StringComparison.OrdinalIgnoreCase);
            if (idx == -1)
                return false;

            var rest = content[idx..];
            if (rest.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || rest.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return true;

            content = rest[4..];
        }

        return false;
    }

    public static string GetThreadName(IUserMessage msg)
    {
        var firstLine = msg.Content.AsSpan();

        var newLine = firstLine.IndexOf('\n');
        if (newLine != -1)
            firstLine = firstLine[..newLine];

        firstLine = firstLine.Trim();

        if (firstLine.IsEmpty)
            return (msg.Author as IGuildUser)?.DisplayName ?? msg.Author.Username;

        if (firstLine.Length > MAX_THREAD_NAME_LENGTH)
            firstLine = firstLine[..MAX_THREAD_NAME_LENGTH];

        return new(firstLine);
    }

    public static ThreadArchiveDuration ToArchiveDuration(int minutes)
        => minutes switch
        {
            AutoThreadArchive.ONE_HOUR => ThreadArchiveDuration.OneHour,
            AutoThreadArchive.THREE_DAYS => ThreadArchiveDuration.ThreeDays,
            AutoThreadArchive.ONE_WEEK => ThreadArchiveDuration.OneWeek,
            _ => ThreadArchiveDuration.OneDay
        };

    public async Task EnableAsync(
        ulong guildId,
        ulong channelId,
        AutoThreadMode mode,
        int archiveMinutes,
        bool includeBots)
    {
        await using var uow = db.GetDbContext();
        await uow.GetTable<AutoThreadChannel>()
                 .InsertOrUpdateAsync(() => new()
                     {
                         GuildId = guildId,
                         ChannelId = channelId,
                         Mode = mode,
                         ArchiveDurationMinutes = archiveMinutes,
                         IncludeBots = includeBots
                     },
                     old => new()
                     {
                         Mode = mode,
                         ArchiveDurationMinutes = archiveMinutes,
                         IncludeBots = includeBots
                     },
                     () => new()
                     {
                         ChannelId = channelId
                     });

        _channels[channelId] = new(guildId, mode, ToArchiveDuration(archiveMinutes), includeBots);
    }

    public async Task<bool> DisableAsync(ulong guildId, ulong channelId)
    {
        _channels.TryRemove(channelId, out _);

        await using var uow = db.GetDbContext();
        var deleted = await uow.GetTable<AutoThreadChannel>()
                               .Where(x => x.GuildId == guildId && x.ChannelId == channelId)
                               .DeleteAsync();

        return deleted > 0;
    }

    public bool IsEnabled(ulong channelId)
        => _channels.ContainsKey(channelId);

    public async Task<IReadOnlyList<AutoThreadChannel>> GetAllAsync(ulong guildId)
    {
        await using var uow = db.GetDbContext();
        return await uow.GetTable<AutoThreadChannel>()
                        .Where(x => x.GuildId == guildId)
                        .OrderBy(x => x.Id)
                        .ToListAsyncLinqToDB();
    }
}

public readonly record struct AutoThreadSetting(
    ulong GuildId,
    AutoThreadMode Mode,
    ThreadArchiveDuration ArchiveDuration,
    bool IncludeBots);

public static class AutoThreadArchive
{
    public const int ONE_HOUR = 60;
    public const int ONE_DAY = 1440;
    public const int THREE_DAYS = 4320;
    public const int ONE_WEEK = 10080;

    public const int DEFAULT = ONE_DAY;

    public static bool TryParse(ReadOnlySpan<char> input, out int minutes)
    {
        input = input.Trim();

        if (input.Equals("1h", StringComparison.InvariantCultureIgnoreCase))
            minutes = ONE_HOUR;
        else if (input.Equals("24h", StringComparison.InvariantCultureIgnoreCase))
            minutes = ONE_DAY;
        else if (input.Equals("3d", StringComparison.InvariantCultureIgnoreCase))
            minutes = THREE_DAYS;
        else if (input.Equals("7d", StringComparison.InvariantCultureIgnoreCase))
            minutes = ONE_WEEK;
        else
        {
            minutes = DEFAULT;
            return false;
        }

        return true;
    }

    public static string Pretty(int minutes)
        => minutes switch
        {
            ONE_HOUR => "1h",
            THREE_DAYS => "3d",
            ONE_WEEK => "7d",
            _ => "24h"
        };
}
