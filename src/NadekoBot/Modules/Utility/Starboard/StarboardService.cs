using LinqToDB;
using LinqToDB.Data;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Modules.Utility.Starboard.Db;
using System.Collections.Frozen;
using System.Net;
using System.Text;

namespace NadekoBot.Modules.Utility.Starboard;

public sealed class StarboardService(
    DbService db,
    DiscordSocketClient client,
    ShardData shardData,
    IMessageSenderService sender,
    IBotStrings strings) : INService, IReadyExecutor
{
    private const int MAX_CONTENT_LENGTH = 2048;
    private const int MIN_CONTENT_LENGTH = 32;
    private const int MAX_QUOTE_LENGTH = 128;
    private const int MAX_FIELD_LENGTH = 256;
    private const int MAX_FILENAME_LENGTH = 64;
    private const int REACTION_USER_FETCH_LIMIT = 100;
    private const int LOCK_STRIPES = 64;
    private static readonly TimeSpan _syncInterval = TimeSpan.FromSeconds(2);
    private static readonly Color _starColor = new(0xFF, 0xAC, 0x33);

    private ConcurrentDictionary<ulong, StarboardSettings> _configs = new();
    private ConcurrentDictionary<ulong, FrozenSet<ulong>> _ignoredChannels = new();

    // The star count is read from the message itself, so a burst of reactions on one
    // message collapses into a single sync.
    private ConcurrentDictionary<ulong, DirtyMessage> _dirty = new();

    // Only the sync loop writes, so these serialize the rare admin operations against it.
    private readonly SemaphoreSlim[] _guildLocks = CreateLocks();

    private static SemaphoreSlim[] CreateLocks()
    {
        var locks = new SemaphoreSlim[LOCK_STRIPES];
        for (var i = 0; i < locks.Length; i++)
            locks[i] = new(1, 1);

        return locks;
    }

    // Nothing is dropped when the bot leaves a guild, so a re-invite keeps the settings.
    public async Task OnReadyAsync()
    {
        await using var uow = db.GetDbContext();

        var configs = await uow.GetTable<StarboardConfig>()
                               .Where(Queries.GuildOnShard<StarboardConfig>(x => x.GuildId,
                                   shardData.TotalShards,
                                   shardData.ShardId))
                               .ToListAsyncLinqToDB();

        var dict = new ConcurrentDictionary<ulong, StarboardSettings>();
        foreach (var config in configs)
            dict[config.GuildId] = StarboardSettings.From(config);

        _configs = dict;

        var ignored = await uow.GetTable<StarboardIgnoredChannel>()
                               .Where(Queries.GuildOnShard<StarboardIgnoredChannel>(x => x.GuildId,
                                   shardData.TotalShards,
                                   shardData.ShardId))
                               .ToListAsyncLinqToDB();

        var ignoredDict = new ConcurrentDictionary<ulong, FrozenSet<ulong>>();
        foreach (var group in ignored.GroupBy(x => x.GuildId))
            ignoredDict[group.Key] = group.Select(x => x.ChannelId).ToFrozenSet();

        _ignoredChannels = ignoredDict;

        client.ReactionAdded += OnReactionChangedAsync;
        client.ReactionRemoved += OnReactionChangedAsync;
        client.ChannelDestroyed += OnChannelDestroyedAsync;

        _ = Task.Run(SyncLoopAsync);
    }

    // A deleted board channel is handled by the sync, which also catches a deletion made while offline.
    private async Task OnChannelDestroyedAsync(SocketChannel channel)
    {
        if (channel is not SocketTextChannel tch)
            return;

        var guildId = tch.Guild.Id;
        var channelId = tch.Id;

        if (!_ignoredChannels.TryGetValue(guildId, out var ignored) || !ignored.Contains(channelId))
            return;

        await using var uow = db.GetDbContext();
        await uow.GetTable<StarboardIgnoredChannel>()
                 .Where(x => x.GuildId == guildId && x.ChannelId == channelId)
                 .DeleteAsync();

        _ignoredChannels[guildId] = ignored.Where(x => x != channelId).ToFrozenSet();
    }

    private async Task DisableBoardInternalAsync(ulong guildId, StarboardSettings settings)
    {
        await using var uow = db.GetDbContext();
        await uow.GetTable<StarboardConfig>()
                 .Where(x => x.GuildId == guildId)
                 .Set(x => x.IsEnabled, false)
                 .UpdateAsync();

        _configs[guildId] = settings with { IsEnabled = false };
    }

    private Task OnReactionChangedAsync(
        Cacheable<IUserMessage, ulong> cachedMsg,
        Cacheable<IMessageChannel, ulong> cachedChannel,
        SocketReaction reaction)
    {
        if (reaction.User.IsSpecified && (reaction.User.Value.IsBot || reaction.User.Value.IsWebhook))
            return Task.CompletedTask;

        if (reaction.Channel is not ITextChannel channel || channel is IThreadChannel)
            return Task.CompletedTask;

        if (!_configs.TryGetValue(channel.GuildId, out var settings) || !settings.IsEnabled)
            return Task.CompletedTask;

        if (channel.Id == settings.ChannelId)
            return Task.CompletedTask;

        if (_ignoredChannels.TryGetValue(channel.GuildId, out var ignored) && ignored.Contains(channel.Id))
            return Task.CompletedTask;

        if (!EmoteEquals(reaction.Emote, settings.Emote))
            return Task.CompletedTask;

        MarkDirty(cachedMsg.Id, channel.GuildId, channel.Id);

        return Task.CompletedTask;
    }

    public void MarkDirty(ulong messageId, ulong guildId, ulong channelId)
        => _dirty[messageId] = new(guildId, channelId);

    public int DirtyCount
        => _dirty.Count;

    public ConcurrentDictionary<ulong, DirtyMessage> DrainDirty()
        => Interlocked.Exchange(ref _dirty, new());

    private async Task SyncLoopAsync()
    {
        using var timer = new PeriodicTimer(_syncInterval);

        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                await SyncTickInternalAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error syncing the starboard");
            }
        }
    }

    private async Task SyncTickInternalAsync()
    {
        if (_dirty.IsEmpty)
            return;

        // A reaction which arrives during the tick lands in the new dictionary.
        var batch = DrainDirty();

        if (batch.IsEmpty)
            return;

        var byGuild = new Dictionary<ulong, List<(ulong ChannelId, ulong MessageId)>>();

        foreach (var (messageId, dirty) in batch)
        {
            if (!byGuild.TryGetValue(dirty.GuildId, out var list))
                byGuild[dirty.GuildId] = list = [];

            list.Add((dirty.ChannelId, messageId));
        }

        var tasks = new List<Task>(byGuild.Count);

        foreach (var (guildId, messages) in byGuild)
            tasks.Add(SyncGuildInternalAsync(guildId, messages));

        await Task.WhenAll(tasks);
    }

    private async Task SyncGuildInternalAsync(ulong guildId, List<(ulong ChannelId, ulong MessageId)> messages)
    {
        try
        {
            if (!_configs.TryGetValue(guildId, out var settings) || !settings.IsEnabled)
                return;

            // A guild which is not in the cache is only resuming, so the board is not touched.
            if (client.GetGuild(guildId) is not { } guild)
                return;

            // The cache holds every channel of a cached guild, so a missing one no longer exists.
            if (guild.GetTextChannel(settings.ChannelId) is not { } sbChannel)
            {
                await DisableBoardInternalAsync(guildId, settings);
                return;
            }

            var botPerms = sbChannel.Guild.CurrentUser.GetPermissions(sbChannel);

            if (!botPerms.SendMessages || !botPerms.EmbedLinks)
                return;

            var sem = _guildLocks[guildId % LOCK_STRIPES];
            await sem.WaitAsync();
            try
            {
                await using var uow = db.GetDbContext();

                var touched = PositionRange.None;

                foreach (var (channelId, messageId) in messages)
                    touched = touched.Union(await SyncMessageInternalAsync(uow,
                        sbChannel,
                        guildId,
                        channelId,
                        messageId,
                        settings));

                if (touched.IsSome)
                    await RenderRangeInternalAsync(uow, sbChannel, guildId, touched.From, touched.To, settings);
            }
            finally
            {
                sem.Release();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error syncing the starboard of guild {GuildId}", guildId);
        }
    }

    private async Task<PositionRange> SyncMessageInternalAsync(
        NadekoContext uow,
        SocketTextChannel sbChannel,
        ulong guildId,
        ulong channelId,
        ulong messageId,
        StarboardSettings settings)
    {
        var entry = await uow.GetTable<StarboardEntry>()
                             .Where(x => x.GuildId == guildId && x.MessageId == messageId)
                             .FirstOrDefaultAsyncLinqToDB();

        if (client.GetChannel(channelId) is not ITextChannel channel)
            return PositionRange.None;

        if (await GetMessageInternalAsync(channel, messageId) is not { } message)
            return PositionRange.None;

        if (!settings.AllowBots && (message.Author.IsBot || message.Author.IsWebhook))
            return PositionRange.None;

        if (channel.IsNsfw && !sbChannel.IsNsfw)
            return PositionRange.None;

        var starCount = await GetStarCountAsync(message, settings, entry is not null);

        if (starCount >= settings.Threshold)
        {
            return entry is null
                ? await InsertEntryInternalAsync(uow, guildId, message, channel, starCount, settings)
                : await MoveEntryInternalAsync(uow, entry, starCount);
        }

        if (entry is null)
            return PositionRange.None;

        // Hysteresis: a posted message stays until it drops a full star below the threshold.
        return starCount >= RemoveThreshold(settings.Threshold)
            ? await MoveEntryInternalAsync(uow, entry, starCount)
            : await DeleteEntryInternalAsync(uow, entry);
    }

    public static int RemoveThreshold(int threshold)
        => Math.Max(1, threshold - 1);

    // An entry lands at the bottom of the block with the same count, so one star moves it one slot.
    private static Task<int> GetTargetPositionAsync(
        NadekoContext uow,
        ulong guildId,
        int starCount,
        int excludeEntryId)
        => uow.GetTable<StarboardEntry>()
              .Where(x => x.GuildId == guildId && x.Id != excludeEntryId && x.StarCount >= starCount)
              .CountAsyncLinqToDB();

    private static async Task<PositionRange> InsertEntryInternalAsync(
        NadekoContext uow,
        ulong guildId,
        IUserMessage message,
        ITextChannel channel,
        int starCount,
        StarboardSettings settings)
    {
        var target = await GetTargetPositionAsync(uow, guildId, starCount, 0);

        // The board is full and the message does not beat the lowest entry on it.
        if (target >= settings.Limit)
            return PositionRange.None;

        var total = await uow.GetTable<StarboardEntry>()
                             .Where(x => x.GuildId == guildId)
                             .CountAsyncLinqToDB();

        // Every entry from the target down moves one slot, so the lowest one can fall off.
        if (total >= settings.Limit)
        {
            var evicted = await uow.GetTable<StarboardEntry>()
                                   .Where(x => x.GuildId == guildId)
                                   .OrderByDescending(static x => x.Position)
                                   .FirstOrDefaultAsyncLinqToDB();

            if (evicted is not null)
            {
                await uow.GetTable<StarboardEntry>()
                         .Where(x => x.Id == evicted.Id)
                         .DeleteAsync();

                total--;
            }
        }

        await uow.GetTable<StarboardEntry>()
                 .Where(x => x.GuildId == guildId && x.Position >= target)
                 .Set(x => x.Position, x => x.Position + 1)
                 .UpdateAsync();

        var messageId = message.Id;
        var channelId = channel.Id;

        await uow.GetTable<StarboardEntry>()
                 .InsertAsync(() => new()
                 {
                     GuildId = guildId,
                     ChannelId = channelId,
                     MessageId = messageId,
                     StarCount = starCount,
                     Position = target
                 });

        return PositionRange.Of(target, total);
    }

    private static async Task<PositionRange> MoveEntryInternalAsync(
        NadekoContext uow,
        StarboardEntry entry,
        int starCount)
    {
        if (entry.StarCount == starCount)
            return PositionRange.None;

        var guildId = entry.GuildId;
        var from = entry.Position;
        var target = await GetTargetPositionAsync(uow, guildId, starCount, entry.Id);

        await uow.GetTable<StarboardEntry>()
                 .Where(x => x.Id == entry.Id)
                 .Set(x => x.StarCount, starCount)
                 .Set(x => x.Position, target)
                 .UpdateAsync();

        if (target != from)
        {
            // Everything between the old and the new slot shifts by one to close the gap.
            if (target < from)
            {
                await uow.GetTable<StarboardEntry>()
                         .Where(x => x.GuildId == guildId
                                     && x.Id != entry.Id
                                     && x.Position >= target
                                     && x.Position < from)
                         .Set(x => x.Position, x => x.Position + 1)
                         .UpdateAsync();
            }
            else
            {
                await uow.GetTable<StarboardEntry>()
                         .Where(x => x.GuildId == guildId
                                     && x.Id != entry.Id
                                     && x.Position > from
                                     && x.Position <= target)
                         .Set(x => x.Position, x => x.Position - 1)
                         .UpdateAsync();
            }
        }

        return PositionRange.Of(from, target);
    }

    private static async Task<PositionRange> DeleteEntryInternalAsync(NadekoContext uow, StarboardEntry entry)
    {
        var guildId = entry.GuildId;
        var from = entry.Position;

        var deleted = await uow.GetTable<StarboardEntry>()
                               .Where(x => x.Id == entry.Id)
                               .DeleteAsync();

        if (deleted == 0)
            return PositionRange.None;

        await uow.GetTable<StarboardEntry>()
                 .Where(x => x.GuildId == guildId && x.Position > from)
                 .Set(x => x.Position, x => x.Position - 1)
                 .UpdateAsync();

        var last = await uow.GetTable<StarboardEntry>()
                            .Where(x => x.GuildId == guildId)
                            .OrderByDescending(static x => x.Position)
                            .Select(static x => (int?)x.Position)
                            .FirstOrDefaultAsyncLinqToDB();

        return PositionRange.Of(from, last ?? from);
    }

    public static int MessageIndexOf(int position)
        => position / StarboardConsts.MAX_EMBEDS_PER_MESSAGE;

    public static int MessageCountFor(int entryCount)
        => (entryCount + StarboardConsts.MAX_EMBEDS_PER_MESSAGE - 1) / StarboardConsts.MAX_EMBEDS_PER_MESSAGE;

    // Rewrites the starboard messages holding the range and drops the ones left without entries.
    private async Task RenderRangeInternalAsync(
        NadekoContext uow,
        ITextChannel sbChannel,
        ulong guildId,
        int fromPosition,
        int toPosition,
        StarboardSettings settings)
    {
        var total = await uow.GetTable<StarboardEntry>()
                             .Where(x => x.GuildId == guildId)
                             .CountAsyncLinqToDB();

        var neededCount = MessageCountFor(total);
        var firstIndex = MessageIndexOf(Math.Min(fromPosition, toPosition));
        var lastIndex = MessageIndexOf(Math.Max(fromPosition, toPosition));

        if (neededCount > 0 && lastIndex > neededCount - 1)
            lastIndex = neededCount - 1;

        // A shifted entry keeps its embed, so it is reused instead of fetching the message again.
        var view = await LoadBoardViewInternalAsync(uow, sbChannel, guildId, firstIndex, lastIndex);

        for (var index = firstIndex; index <= lastIndex; index++)
            await RenderMessageInternalAsync(uow, sbChannel, guildId, index, settings, view);

        await TrimMessagesInternalAsync(uow, sbChannel, guildId, neededCount);
    }

    // Indexes the embeds the touched messages already hold, by the id of the starred message.
    private static async Task<BoardView> LoadBoardViewInternalAsync(
        NadekoContext uow,
        ITextChannel sbChannel,
        ulong guildId,
        int firstIndex,
        int lastIndex)
    {
        var view = new BoardView();

        var rows = await uow.GetTable<StarboardMessage>()
                            .Where(x => x.GuildId == guildId
                                        && x.Index >= firstIndex
                                        && x.Index <= lastIndex)
                            .ToListAsyncLinqToDB();

        foreach (var row in rows)
        {
            if (await GetStarboardMessageInternalAsync(sbChannel, row.MessageId) is not { } sbMsg)
                continue;

            view.Messages[row.Index] = sbMsg;

            foreach (var embed in sbMsg.Embeds)
            {
                if (embed is Embed e && TryGetEntryId(e) is { } starredId)
                    view.Embeds[starredId] = e;
            }
        }

        return view;
    }

    private sealed class BoardView
    {
        public Dictionary<int, IUserMessage> Messages { get; } = new();
        public Dictionary<ulong, Embed> Embeds { get; } = new();
    }

    private async Task RenderMessageInternalAsync(
        NadekoContext uow,
        ITextChannel sbChannel,
        ulong guildId,
        int index,
        StarboardSettings settings,
        BoardView view)
    {
        var first = index * StarboardConsts.MAX_EMBEDS_PER_MESSAGE;

        var entries = await uow.GetTable<StarboardEntry>()
                               .Where(x => x.GuildId == guildId
                                           && x.Position >= first
                                           && x.Position < first + StarboardConsts.MAX_EMBEDS_PER_MESSAGE)
                               .OrderBy(static x => x.Position)
                               .ToListAsyncLinqToDB();

        if (entries.Count == 0)
            return;

        var embeds = new List<Embed>(entries.Count);

        foreach (var entry in entries)
        {
            var hasCached = view.Embeds.TryGetValue(entry.MessageId, out var cached);

            if (hasCached && StarCountOf(cached!) == entry.StarCount)
            {
                embeds.Add(cached!);
                continue;
            }

            if (client.GetChannel(entry.ChannelId) is ITextChannel channel
                && await GetMessageInternalAsync(channel, entry.MessageId) is { } message)
            {
                embeds.Add(BuildStarboardEmbed(message, channel, entry.StarCount, settings.FooterEmote).Build());
                continue;
            }

            // The starred message is gone, so the existing embed is kept and the post stays.
            if (hasCached)
                embeds.Add(cached!);
        }

        if (embeds.Count == 0)
            return;

        await PostMessageInternalAsync(uow, sbChannel, guildId, index, embeds.ToArray(), view);
    }

    private async Task PostMessageInternalAsync(
        NadekoContext uow,
        ITextChannel sbChannel,
        ulong guildId,
        int index,
        Embed[] embeds,
        BoardView view)
    {
        if (view.Messages.TryGetValue(index, out var existing)
            && await TryModifyEmbedsInternalAsync(existing, embeds))
            return;

        var builders = new EmbedBuilder[embeds.Length];
        for (var i = 0; i < embeds.Length; i++)
            builders[i] = embeds[i].ToEmbedBuilder();

        var sent = await sender.Response(sbChannel).Embeds(builders).SendAsync();
        var sentId = sent.Id;

        await uow.GetTable<StarboardMessage>()
                 .InsertOrUpdateAsync(() => new()
                     {
                         GuildId = guildId,
                         Index = index,
                         MessageId = sentId
                     },
                     old => new()
                     {
                         MessageId = sentId
                     },
                     () => new()
                     {
                         GuildId = guildId,
                         Index = index
                     });
    }

    private async Task TrimMessagesInternalAsync(
        NadekoContext uow,
        ITextChannel sbChannel,
        ulong guildId,
        int neededCount)
    {
        var stale = await uow.GetTable<StarboardMessage>()
                             .Where(x => x.GuildId == guildId && x.Index >= neededCount)
                             .ToListAsyncLinqToDB();

        if (stale.Count == 0)
            return;

        foreach (var msg in stale)
        {
            if (await GetStarboardMessageInternalAsync(sbChannel, msg.MessageId) is { } sbMsg)
                await TryDeleteStarboardMessageInternalAsync(sbMsg);
        }

        await uow.GetTable<StarboardMessage>()
                 .Where(x => x.GuildId == guildId && x.Index >= neededCount)
                 .DeleteAsync();
    }

    // The footer of every starboard embed is "{emote} {count} | {starred message id}".
    public static ulong? TryGetEntryId(Embed embed)
    {
        if (embed.Footer?.Text is not { } text)
            return null;

        var idx = text.LastIndexOf('|');

        return idx >= 0 && ulong.TryParse(text.AsSpan(idx + 1).Trim(), out var id)
            ? id
            : null;
    }

    public static int StarCountOf(Embed embed)
    {
        if (embed.Footer?.Text is not { } text)
            return -1;

        var end = text.LastIndexOf('|');

        if (end < 0)
            return -1;

        var span = text.AsSpan(0, end).TrimEnd();
        var start = span.LastIndexOf(' ');

        return int.TryParse(span[(start + 1)..], out var count) ? count : -1;
    }

    private static async Task<IUserMessage?> GetMessageInternalAsync(ITextChannel channel, ulong messageId)
    {
        try
        {
            return await channel.GetMessageAsync(messageId) as IUserMessage;
        }
        catch (HttpException ex) when (ex.HttpCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            return null;
        }
    }

    private static async Task<int> GetStarCountAsync(
        IUserMessage message,
        StarboardSettings settings,
        bool hasEntry)
    {
        var count = 0;
        IEmote? matched = null;

        foreach (var (emote, meta) in message.Reactions)
        {
            if (!EmoteEquals(emote, settings.Emote))
                continue;

            matched = emote;
            count = meta.ReactionCount;
            break;
        }

        if (matched is null || count == 0)
            return 0;

        if (settings.AllowSelfStar)
            return count;

        // The author's own reaction only matters near the threshold or for a posted message.
        if (count < (hasEntry ? RemoveThreshold(settings.Threshold) : settings.Threshold))
            return count;

        var reactors = await message.GetReactionUsersAsync(matched, REACTION_USER_FETCH_LIMIT).FlattenAsync();
        foreach (var reactor in reactors)
        {
            if (reactor.Id == message.Author.Id)
                return count - 1;
        }

        return count;
    }

    private EmbedBuilder BuildStarboardEmbed(
        IUserMessage message,
        ITextChannel channel,
        int starCount,
        string footerEmote)
    {
        // Ten embeds share one message budget, so the content takes only what is left over.
        var eb = sender.CreateEmbed(channel.GuildId)
                       .WithAuthor(message.Author.ToString(),
                           message.Author.GetAvatarUrl() ?? message.Author.GetDefaultAvatarUrl())
                       .WithColor(_starColor)
                       .WithFooter($"{footerEmote} {starCount} | {message.Id}")
                       .WithTimestamp(message.CreatedAt)
                       .AddField(strings.GetText(strs.starboard_source, channel.GuildId),
                           $"[#{channel.Name}]({message.GetJumpUrl()})");

        if (message.ReferencedMessage is { } replied && !string.IsNullOrWhiteSpace(replied.Content))
        {
            eb.AddField(strings.GetText(strs.starboard_replying_to(replied.Author.ToString()), channel.GuildId),
                replied.Content.TrimTo(MAX_QUOTE_LENGTH));
        }

        var imageUrl = GetImageUrl(message);

        if (imageUrl is not null)
            eb.WithImageUrl(imageUrl);

        if (BuildAttachmentList(message, imageUrl) is { } attachments)
            eb.AddField(strings.GetText(strs.starboard_attachments, channel.GuildId), attachments);

        if (!string.IsNullOrWhiteSpace(message.Content))
        {
            var available = Math.Min(MAX_CONTENT_LENGTH, StarboardConsts.EMBED_CHAR_BUDGET - eb.Length);

            if (available >= MIN_CONTENT_LENGTH)
                eb.WithDescription(message.Content.TrimTo(available));
        }

        return eb;
    }

    private static string? BuildAttachmentList(IUserMessage message, string? shownImageUrl)
    {
        if (message.Attachments.Count == 0)
            return null;

        var sb = new StringBuilder();

        foreach (var attachment in message.Attachments)
        {
            if (attachment.Url == shownImageUrl)
                continue;

            var line = $"[{attachment.Filename.TrimTo(MAX_FILENAME_LENGTH)}]({attachment.Url})";

            if (sb.Length + line.Length + 1 > MAX_FIELD_LENGTH)
                break;

            if (sb.Length > 0)
                sb.Append('\n');

            sb.Append(line);
        }

        return sb.Length == 0 ? null : sb.ToString();
    }

    private static string? GetImageUrl(IUserMessage message)
    {
        foreach (var attachment in message.Attachments)
        {
            if (attachment.ContentType?.StartsWith("image/", StringComparison.InvariantCultureIgnoreCase) == true)
                return attachment.Url;
        }

        foreach (var embed in message.Embeds)
        {
            if (embed.Image is { } image)
                return image.Url;

            if (embed.Thumbnail is { } thumb)
                return thumb.Url;
        }

        return null;
    }

    private static async Task<IUserMessage?> GetStarboardMessageInternalAsync(
        ITextChannel sbChannel,
        ulong starboardMessageId)
    {
        try
        {
            return await sbChannel.GetMessageAsync(starboardMessageId) as IUserMessage;
        }
        catch (HttpException ex) when (ex.HttpCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            return null;
        }
    }

    private static async Task<bool> TryModifyEmbedsInternalAsync(IUserMessage sbMsg, Embed[] embeds)
    {
        try
        {
            await sbMsg.ModifyAsync(x => x.Embeds = embeds);
            return true;
        }
        catch (HttpException ex) when (ex.HttpCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            return false;
        }
    }

    private static async Task TryDeleteStarboardMessageInternalAsync(IUserMessage sbMsg)
    {
        try
        {
            await sbMsg.DeleteAsync();
        }
        catch (HttpException ex) when (ex.HttpCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
        }
    }

    // A guild which already has a board in another channel moves its entries to the new one.
    public async Task SetChannelAsync(ulong guildId, ulong channelId)
    {
        await using var uow = db.GetDbContext();

        var previousChannelId = _configs.TryGetValue(guildId, out var previous) ? previous.ChannelId : channelId;

        await uow.GetTable<StarboardConfig>()
                 .InsertOrUpdateAsync(() => new()
                     {
                         GuildId = guildId,
                         ChannelId = channelId,
                         Emote = StarboardConsts.DEFAULT_EMOTE,
                         Threshold = StarboardConsts.DEFAULT_THRESHOLD,
                         AllowSelfStar = false,
                         IsEnabled = true,
                         Limit = StarboardConsts.DEFAULT_LIMIT
                     },
                     old => new()
                     {
                         ChannelId = channelId
                     },
                     () => new()
                     {
                         GuildId = guildId
                     });

        _configs.AddOrUpdate(guildId,
            static (_, chId) => new(chId,
                StarboardConsts.DEFAULT_EMOTE,
                ParseEmote(StarboardConsts.DEFAULT_EMOTE)!,
                StarboardConsts.DEFAULT_THRESHOLD,
                false,
                false,
                true,
                StarboardConsts.DEFAULT_LIMIT),
            static (_, old, chId) => old with { ChannelId = chId },
            channelId);

        if (previousChannelId != channelId)
            await MoveBoardInternalAsync(uow, guildId, _configs[guildId]);
    }

    // The old messages stay where they are. The bot only forgets them, so the channel keeps an archive.
    private async Task MoveBoardInternalAsync(
        NadekoContext uow,
        ulong guildId,
        StarboardSettings settings)
    {
        var sem = _guildLocks[guildId % LOCK_STRIPES];
        await sem.WaitAsync();
        try
        {
            await uow.GetTable<StarboardMessage>()
                     .Where(x => x.GuildId == guildId)
                     .DeleteAsync();

            var entries = await uow.GetTable<StarboardEntry>()
                                   .Where(x => x.GuildId == guildId)
                                   .OrderBy(static x => x.Position)
                                   .ToListAsyncLinqToDB();

            // The new board is built from scratch, so an entry which can not be fetched is dropped.
            var position = 0;

            foreach (var entry in entries)
            {
                if (client.GetChannel(entry.ChannelId) is ITextChannel channel
                    && await GetMessageInternalAsync(channel, entry.MessageId) is not null)
                {
                    if (entry.Position != position)
                    {
                        var newPosition = position;
                        await uow.GetTable<StarboardEntry>()
                                 .Where(x => x.Id == entry.Id)
                                 .Set(x => x.Position, newPosition)
                                 .UpdateAsync();
                    }

                    position++;
                    continue;
                }

                await uow.GetTable<StarboardEntry>()
                         .Where(x => x.Id == entry.Id)
                         .DeleteAsync();
            }

            if (position == 0 || client.GetChannel(settings.ChannelId) is not ITextChannel sbChannel)
                return;

            await RenderRangeInternalAsync(uow, sbChannel, guildId, 0, position - 1, settings);
        }
        finally
        {
            sem.Release();
        }
    }

    public async Task<bool> SetThresholdAsync(ulong guildId, int threshold)
    {
        if (!_configs.TryGetValue(guildId, out var settings))
            return false;

        await using var uow = db.GetDbContext();
        await uow.GetTable<StarboardConfig>()
                 .Where(x => x.GuildId == guildId)
                 .Set(x => x.Threshold, threshold)
                 .UpdateAsync();

        _configs[guildId] = settings with { Threshold = threshold };
        return true;
    }

    // A lower limit drops the lowest ranked entries right away.
    public async Task<bool> SetLimitAsync(ulong guildId, int limit)
    {
        if (!_configs.TryGetValue(guildId, out var settings))
            return false;

        await using var uow = db.GetDbContext();
        await uow.GetTable<StarboardConfig>()
                 .Where(x => x.GuildId == guildId)
                 .Set(x => x.Limit, limit)
                 .UpdateAsync();

        settings = settings with { Limit = limit };
        _configs[guildId] = settings;

        var sem = _guildLocks[guildId % LOCK_STRIPES];
        await sem.WaitAsync();
        try
        {
            var dropped = await uow.GetTable<StarboardEntry>()
                                   .Where(x => x.GuildId == guildId && x.Position >= limit)
                                   .DeleteAsync();

            if (dropped > 0 && client.GetChannel(settings.ChannelId) is ITextChannel sbChannel)
            {
                var total = await uow.GetTable<StarboardEntry>()
                                     .Where(x => x.GuildId == guildId)
                                     .CountAsyncLinqToDB();

                // A limit inside a message leaves it holding embeds of dropped entries.
                if (total > 0 && limit % StarboardConsts.MAX_EMBEDS_PER_MESSAGE != 0)
                    await RenderRangeInternalAsync(uow, sbChannel, guildId, total - 1, total - 1, settings);

                await TrimMessagesInternalAsync(uow, sbChannel, guildId, MessageCountFor(total));
            }
        }
        finally
        {
            sem.Release();
        }

        return true;
    }

    public async Task<bool> SetEmoteAsync(ulong guildId, string emoteText)
    {
        if (!_configs.TryGetValue(guildId, out var settings))
            return false;

        if (ParseEmote(emoteText) is not { } emote)
            return false;

        await using var uow = db.GetDbContext();
        await uow.GetTable<StarboardConfig>()
                 .Where(x => x.GuildId == guildId)
                 .Set(x => x.Emote, emoteText)
                 .UpdateAsync();

        _configs[guildId] = settings with { EmoteText = emoteText, Emote = emote };
        return true;
    }

    public async Task<bool?> ToggleSelfStarAsync(ulong guildId)
    {
        if (!_configs.TryGetValue(guildId, out var settings))
            return null;

        var newVal = !settings.AllowSelfStar;

        await using var uow = db.GetDbContext();
        await uow.GetTable<StarboardConfig>()
                 .Where(x => x.GuildId == guildId)
                 .Set(x => x.AllowSelfStar, newVal)
                 .UpdateAsync();

        _configs[guildId] = settings with { AllowSelfStar = newVal };
        return newVal;
    }

    public async Task<bool?> ToggleAllowBotsAsync(ulong guildId)
    {
        if (!_configs.TryGetValue(guildId, out var settings))
            return null;

        var newVal = !settings.AllowBots;

        await using var uow = db.GetDbContext();
        await uow.GetTable<StarboardConfig>()
                 .Where(x => x.GuildId == guildId)
                 .Set(x => x.AllowBots, newVal)
                 .UpdateAsync();

        _configs[guildId] = settings with { AllowBots = newVal };
        return newVal;
    }

    public async Task<bool?> ToggleAsync(ulong guildId)
    {
        if (!_configs.TryGetValue(guildId, out var settings))
            return null;

        var newVal = !settings.IsEnabled;

        await using var uow = db.GetDbContext();
        await uow.GetTable<StarboardConfig>()
                 .Where(x => x.GuildId == guildId)
                 .Set(x => x.IsEnabled, newVal)
                 .UpdateAsync();

        _configs[guildId] = settings with { IsEnabled = newVal };
        return newVal;
    }

    public async Task SetIgnoredChannelsAsync(ulong guildId, IReadOnlyCollection<ulong> channelIds)
    {
        await using var uow = db.GetDbContext();
        await using var tx = await uow.Database.BeginTransactionAsync();

        await uow.GetTable<StarboardIgnoredChannel>()
                 .Where(x => x.GuildId == guildId)
                 .DeleteAsync();

        if (channelIds.Count > 0)
        {
            var rows = new List<StarboardIgnoredChannel>(channelIds.Count);
            foreach (var channelId in channelIds)
                rows.Add(new() { GuildId = guildId, ChannelId = channelId });

            await uow.GetTable<StarboardIgnoredChannel>().BulkCopyAsync(rows);
        }

        await tx.CommitAsync();

        if (channelIds.Count == 0)
            _ignoredChannels.TryRemove(guildId, out _);
        else
            _ignoredChannels[guildId] = channelIds.ToFrozenSet();
    }

    public FrozenSet<ulong> GetIgnoredChannels(ulong guildId)
        => _ignoredChannels.TryGetValue(guildId, out var ignored) ? ignored : FrozenSet<ulong>.Empty;

    public async Task<bool> ResetAsync(ulong guildId)
    {
        // Dropped first, so a sync which starts now stops before it touches the tables.
        _configs.TryRemove(guildId, out _);
        _ignoredChannels.TryRemove(guildId, out _);

        await using var uow = db.GetDbContext();

        // A sync which is already running would insert its rows back after the delete.
        var sem = _guildLocks[guildId % LOCK_STRIPES];
        await sem.WaitAsync();
        try
        {
            var deleted = await uow.GetTable<StarboardConfig>()
                                   .Where(x => x.GuildId == guildId)
                                   .DeleteAsync();

            await uow.GetTable<StarboardEntry>()
                     .Where(x => x.GuildId == guildId)
                     .DeleteAsync();

            await uow.GetTable<StarboardIgnoredChannel>()
                     .Where(x => x.GuildId == guildId)
                     .DeleteAsync();

            await uow.GetTable<StarboardMessage>()
                     .Where(x => x.GuildId == guildId)
                     .DeleteAsync();

            return deleted > 0;
        }
        finally
        {
            sem.Release();
        }
    }

    public StarboardSettings? GetSettings(ulong guildId)
        => _configs.TryGetValue(guildId, out var settings) ? settings : null;

    public static bool EmoteEquals(IEmote a, IEmote b)
    {
        if (a is Emote ea)
            return b is Emote eb && ea.Id == eb.Id;

        if (a is Emoji emojiA)
            return b is Emoji emojiB && emojiA.Name == emojiB.Name;

        return false;
    }

    public static IEmote? ParseEmote(string emoteText)
    {
        if (string.IsNullOrWhiteSpace(emoteText) || emoteText.Length > StarboardConsts.MAX_EMOTE_LENGTH)
            return null;

        if (Emote.TryParse(emoteText, out var customEmote))
            return customEmote;

        if (Emoji.TryParse(emoteText, out var emoji))
            return emoji;

        return null;
    }
}

public sealed record StarboardSettings(
    ulong ChannelId,
    string EmoteText,
    IEmote Emote,
    int Threshold,
    bool AllowSelfStar,
    bool AllowBots,
    bool IsEnabled,
    int Limit)
{
    // A custom emote does not render in a footer, so its name is shown instead.
    // Computed, because `with` copies backing fields and would keep a stale value.
    public string FooterEmote => Emote is Emote custom ? custom.Name : EmoteText;

    public static StarboardSettings From(StarboardConfig config)
        => new(config.ChannelId,
            config.Emote,
            StarboardService.ParseEmote(config.Emote) ?? new Emoji(StarboardConsts.DEFAULT_EMOTE),
            config.Threshold,
            config.AllowSelfStar,
            config.AllowBots,
            config.IsEnabled,
            config.Limit);
}

public readonly record struct DirtyMessage(ulong GuildId, ulong ChannelId);

// The board positions a change touched. Merging a whole batch gives the range to render.
public readonly record struct PositionRange(int From, int To, bool IsSome)
{
    public static PositionRange None => default;

    public static PositionRange Of(int a, int b)
        => new(Math.Min(a, b), Math.Max(a, b), true);

    public PositionRange Union(PositionRange other)
    {
        if (!other.IsSome)
            return this;

        if (!IsSome)
            return other;

        return new(Math.Min(From, other.From), Math.Max(To, other.To), true);
    }
}
