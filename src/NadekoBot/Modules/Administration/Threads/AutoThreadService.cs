using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using System.Net;

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

    private ConcurrentDictionary<ulong, AutoThreadSetting> _channels = new();

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
            dict[item.ChannelId] = new(item.GuildId, item.Mode, ToArchiveDuration(item.ArchiveDurationMinutes));

        _channels = dict;

        client.LeftGuild += OnLeftGuildAsync;
        client.ChannelDestroyed += OnChannelDestroyedAsync;
    }

    private async Task OnLeftGuildAsync(SocketGuild guild)
    {
        foreach (var (channelId, setting) in _channels)
        {
            if (setting.GuildId == guild.Id)
                _channels.TryRemove(channelId, out _);
        }

        await using var uow = db.GetDbContext();
        await uow.GetTable<AutoThreadChannel>()
                 .Where(x => x.GuildId == guild.Id)
                 .DeleteAsync();
    }

    private async Task OnChannelDestroyedAsync(SocketChannel channel)
    {
        if (!_channels.TryRemove(channel.Id, out _))
            return;

        await using var uow = db.GetDbContext();
        await uow.GetTable<AutoThreadChannel>()
                 .Where(x => x.ChannelId == channel.Id)
                 .DeleteAsync();
    }

    public async ValueTask ExecOnNoCommandAsync(IGuild? guild, IUserMessage msg)
    {
        if (guild is null)
            return;

        if (msg.Author.IsBot || msg.Author.IsWebhook)
            return;

        if (msg.Channel is not ITextChannel tch || msg.Channel is IThreadChannel)
            return;

        if (!_channels.TryGetValue(tch.Id, out var setting))
            return;

        if (setting.Mode == AutoThreadMode.Media && !HasMedia(msg))
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

            await DisableAsync(tch.GuildId, tch.Id);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to create an automatic thread in channel {ChannelId}", tch.Id);
        }
    }

    public async Task<int> BackfillAsync(
        ITextChannel channel,
        AutoThreadMode mode,
        int count,
        ulong beforeMessageId)
    {
        var fetchLimit = Math.Min(count * BACKFILL_FETCH_MULTIPLIER, MAX_BACKFILL_FETCH);
        var messages = await channel.GetMessagesAsync(beforeMessageId, Direction.Before, fetchLimit)
                                    .FlattenAsync();
        var targets = SelectBackfillTargets(messages, mode, count);

        var created = 0;
        for (var i = 0; i < targets.Count; i++)
        {
            if (i > 0)
                await Task.Delay(BACKFILL_DELAY_MS);

            try
            {
                await channel.CreateThreadAsync(GetThreadName(targets[i]),
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
        int count)
    {
        var targets = new List<IUserMessage>();

        foreach (var msg in messages)
        {
            if (msg is not IUserMessage userMsg)
                continue;

            if (msg.Author.IsBot || msg.Author.IsWebhook)
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
        => msg.Attachments.Count > 0 || ContainsLink(msg.Content);

    private static bool ContainsLink(ReadOnlySpan<char> content)
    {
        while (!content.IsEmpty)
        {
            var idx = content.IndexOf("http", StringComparison.InvariantCultureIgnoreCase);
            if (idx == -1)
                return false;

            var rest = content[idx..];
            if (rest.StartsWith("http://", StringComparison.InvariantCultureIgnoreCase)
                || rest.StartsWith("https://", StringComparison.InvariantCultureIgnoreCase))
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

    public async Task EnableAsync(ulong guildId, ulong channelId, AutoThreadMode mode, int archiveMinutes)
    {
        await using var uow = db.GetDbContext();
        await uow.GetTable<AutoThreadChannel>()
                 .InsertOrUpdateAsync(() => new()
                     {
                         GuildId = guildId,
                         ChannelId = channelId,
                         Mode = mode,
                         ArchiveDurationMinutes = archiveMinutes
                     },
                     old => new()
                     {
                         Mode = mode,
                         ArchiveDurationMinutes = archiveMinutes
                     },
                     () => new()
                     {
                         ChannelId = channelId
                     });

        _channels[channelId] = new(guildId, mode, ToArchiveDuration(archiveMinutes));
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
    ThreadArchiveDuration ArchiveDuration);

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
