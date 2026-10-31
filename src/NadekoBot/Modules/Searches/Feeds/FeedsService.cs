using CodeHollow.FeedReader;
using CodeHollow.FeedReader.Feeds;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Music;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace NadekoBot.Modules.Searches.Services;

public readonly record struct FeedFetchResult(Feed? Feed, int Status, TimeSpan? Freshness, TimeSpan? RetryAfter);

public enum FeedCheckResult
{
    Ok,
    Unreadable,
    Unavailable
}

public sealed partial class FeedsService(
    DbService db,
    DiscordSocketClient client,
    IMessageSenderService sender,
    ShardData shardData,
    SearchesConfigService scs,
    IHttpClientFactory httpFactory,
    MusicServiceClient music,
    TimeProvider? timeProvider = null) : INService, IReadyExecutor
{
    public const string USER_AGENT =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/139.0.0.0 Safari/537.36 OPR/123.0.0.0 (Edition beta)";

    private const int MAX_FEED_BYTES = 4 * 1024 * 1024;
    private const int FEED_TIMEOUT_SECONDS = 15;

    // Posts at most this many items when the bot is behind.
    private const int MAX_ITEMS_PER_CHECK = 3;
    private static readonly TimeSpan _tickInterval = TimeSpan.FromSeconds(10);

    // The service keeps a channel's uploads for 10 minutes, so a new video shows within 15.
    private static readonly TimeSpan _uploadsPollInterval = TimeSpan.FromMinutes(5);
    private const string YT_WATCH_URL = "https://www.youtube.com/watch?v=";
    private const string YT_FEED_PATH = "/feeds/videos.xml";

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly FeedHealth _health = new(timeProvider ?? TimeProvider.System);

    private NonBlocking.ConcurrentDictionary<string, List<FeedSub>> _subs = new();
    private readonly NonBlocking.ConcurrentDictionary<string, DateTime> _lastPosts = new();

    // Only the feed loop uses these.
    private readonly HashSet<int> _streakStarts = new();
    private readonly HashSet<int> _streakEnds = new();
    private readonly List<(string Key, List<FeedSub> Subs)> _due = new();
    private readonly List<Task> _sendTasks = new();
    private readonly Dictionary<string, HashSet<string>?> _seenVideos = new();

    // Read once per tick: on a managed bot the setting comes from a file.
    private bool _serviceFeeds;
    private readonly Dictionary<string, string?> _ytFeedChannels = new();

    public async Task OnReadyAsync()
    {
        await using (var uow = db.GetDbContext())
        {
            var subs = await uow.GetTable<FeedSub>()
                .Where(Queries.GuildOnShard<FeedSub>(x => x.GuildId, shardData.TotalShards, shardData.ShardId))
                .ToListAsyncLinqToDB();

            _subs = subs
                .GroupBy(x => x.Url.ToLower())
                .ToDictionary(x => x.Key, x => x.ToList())
                .ToConcurrent();
        }

        foreach (var kvp in _subs)
            _health.Touch(kvp.Key, kvp.Value[0].Url, GetFailingSince(kvp.Value));

        _ = Task.Run(TrackFeedsLoopInternalAsync);
    }

    public static DateTime? GetFailingSince(List<FeedSub> subs)
    {
        DateTime? min = null;
        foreach (var sub in subs)
        {
            if (sub.FailingSince is { } since && (min is null || since < min))
                min = since;
        }

        return min;
    }

    private DateTime? GetPubDate(FeedItem item)
    {
        if (item.PublishingDate is not null)
            return item.PublishingDate;
        if (item.SpecificItem is AtomFeedItem atomItem)
            return atomItem.UpdatedDate;
        return null;
    }

    /// <summary>
    /// Builds an embed from a parsed feed item, extracting title, description, link, and thumbnail.
    /// </summary>
    public static EmbedBuilder BuildFeedEmbed(EmbedBuilder embed, FeedItem feedItem, string rssUrl)
    {
        embed.WithFooter(rssUrl);

        var link = feedItem.SpecificItem.Link;
        if (!string.IsNullOrWhiteSpace(link) && Uri.IsWellFormedUriString(link, UriKind.Absolute))
            embed.WithUrl(link);

        var title = string.IsNullOrWhiteSpace(feedItem.Title) ? "-" : feedItem.Title;

        var gotImage = false;
        if (feedItem.SpecificItem is MediaRssFeedItem mrfi
            && (mrfi.Enclosure?.MediaType?.StartsWith("image/") ?? false))
        {
            var imgUrl = mrfi.Enclosure.Url;
            if (!string.IsNullOrWhiteSpace(imgUrl)
                && Uri.IsWellFormedUriString(imgUrl, UriKind.Absolute))
            {
                embed.WithImageUrl(imgUrl);
                gotImage = true;
            }
        }

        if (!gotImage && feedItem.SpecificItem is AtomFeedItem afi)
        {
            var previewElement = afi.Element.Descendants()
                .FirstOrDefault(x => x.Name.LocalName == "preview");

            if (previewElement is null)
            {
                previewElement = afi.Element.Descendants()
                    .FirstOrDefault(x => x.Name.LocalName == "thumbnail");
            }

            if (previewElement is not null)
            {
                var urlAttribute = previewElement.Attribute("url");
                if (urlAttribute is not null
                    && !string.IsNullOrWhiteSpace(urlAttribute.Value)
                    && Uri.IsWellFormedUriString(urlAttribute.Value, UriKind.Absolute))
                {
                    embed.WithImageUrl(urlAttribute.Value);
                }
            }
        }

        embed.WithTitle(title.TrimTo(256));

        var desc = feedItem.Description?.StripHtml();
        if (!string.IsNullOrWhiteSpace(desc))
            embed.WithDescription(desc.TrimTo(2048));

        return embed;
    }

    private async Task TrackFeedsLoopInternalAsync()
    {
        using var timer = new PeriodicTimer(_tickInterval);
        do
        {
            try
            {
                await TrackFeedsTickInternalAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in the feed loop");
            }
            finally
            {
                _due.Clear();
                _sendTasks.Clear();
            }
        } while (await timer.WaitForNextTickAsync());
    }

    private async Task TrackFeedsTickInternalAsync()
    {
        _health.BeginTick();
        _serviceFeeds = music.IsConfigured;
        foreach (var kvp in _subs)
        {
            if (kvp.Value.Count == 0)
                continue;

            // The feeds which the music service reads share one host, apart from youtube.com.
            var url = kvp.Value[0].Url;
            _health.Touch(kvp.Key,
                url,
                sourceHost: GetServiceChannelInternal(kvp.Key, url) is null ? null : MusicServiceClient.HTTP_CLIENT_NAME);
            if (_health.IsDue(kvp.Key))
                _due.Add((kvp.Key, kvp.Value));
        }

        _health.EndTick();

        foreach (var (key, subs) in _due)
        {
            // A feed checked earlier in this tick may have paused the host.
            if (!_health.IsDue(key))
                continue;

            try
            {
                await CheckFeedInternalAsync(key, subs);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error checking feed {FeedUrl}", subs[0].Url);
            }
        }

        await Task.WhenAll(_sendTasks);
        await SaveStreaksAsync(db, _streakStarts, _streakEnds, _time.GetUtcNow().UtcDateTime);
    }

    private async Task CheckFeedInternalAsync(string key, List<FeedSub> subs)
    {
        var rssUrl = subs[0].Url;
        if (GetServiceChannelInternal(key, rssUrl) is { } channelId)
        {
            await CheckUploadsInternalAsync(key, subs, rssUrl, channelId);
            return;
        }

        var result = await FetchFeedAsync(rssUrl);
        if (result.Feed is not { } feed)
        {
            await OnFeedFailureInternalAsync(key, subs, result.Status, result.RetryAfter);
            return;
        }

        if (_health.OnSuccess(key, result.Freshness))
            MarkStreakInternal(subs, _streakEnds, _streakStarts);

        var items = new List<(FeedItem Item, DateTime LastUpdate)>(MAX_ITEMS_PER_CHECK);
        foreach (var item in feed.Items)
        {
            var pubDate = GetPubDate(item);

            if (pubDate is null)
                continue;

            items.Add((item, pubDate.Value.ToUniversalTime()));

            if (items.Count >= MAX_ITEMS_PER_CHECK)
                break;
        }

        if (items.Count == 0)
            return;

        if (!_lastPosts.TryGetValue(key, out var lastFeedUpdate))
        {
            lastFeedUpdate = _lastPosts[key] = items[0].LastUpdate;
        }

        var deadSubs = new List<FeedSub>();

        for (var index = 1; index <= items.Count; index++)
        {
            var (feedItem, itemUpdateDate) = items[^index];
            if (itemUpdateDate <= lastFeedUpdate)
                continue;

            _lastPosts[key] = itemUpdateDate;
            QueuePostInternal(subs, BuildFeedEmbed(sender.CreateEmbed(), feedItem, rssUrl), deadSubs);
        }

        await RemoveLeftGuildSubsInternalAsync(key, rssUrl, deadSubs);
    }

    // YouTube answers feeds/videos.xml with 404 to many hosts, so a bot with the music service reads the channel's
    // uploads there. Only a missing channel counts as a failure; a service error keeps the streak as it is.
    private async Task CheckUploadsInternalAsync(string key, List<FeedSub> subs, string rssUrl, string channelId)
    {
        var res = await music.GetUploadsAsync(channelId);
        if (res.Status == MusicServiceStatus.NotFound)
        {
            await OnFeedFailureInternalAsync(key, subs, (int)HttpStatusCode.NotFound, null);
            return;
        }

        if (res.Status != MusicServiceStatus.Ok)
        {
            _health.OnUnavailable(key);
            return;
        }

        if (_health.OnSuccess(key, _uploadsPollInterval))
            MarkStreakInternal(subs, _streakEnds, _streakStarts);

        var videos = res.Videos;
        ref var seen = ref CollectionsMarshal.GetValueRefOrAddDefault(_seenVideos, key, out _);
        var count = TakeNewVideos(videos, ref seen);
        if (count == 0)
            return;

        var deadSubs = new List<FeedSub>();
        for (var i = count - 1; i >= 0; i--)
            QueuePostInternal(subs, BuildVideoEmbed(sender.CreateEmbed(), videos[i], rssUrl), deadSubs);

        await RemoveLeftGuildSubsInternalAsync(key, rssUrl, deadSubs);
    }

    // How many of the newest videos to post, at most MAX_ITEMS_PER_CHECK, and the ids to remember. The first read
    // after a start or an add only learns the list, also an empty one. A later empty answer keeps the known ids.
    public static int TakeNewVideos(MusicServiceUpload[] videos, ref HashSet<string>? seen)
    {
        if (seen is null)
        {
            seen = new(videos.Length, StringComparer.Ordinal);
            AddVideoIdsInternal(seen, videos);
            return 0;
        }

        if (videos.Length == 0)
            return 0;

        var count = Math.Min(CountNewVideosInternal(videos, seen), MAX_ITEMS_PER_CHECK);
        seen.Clear();
        AddVideoIdsInternal(seen, videos);
        return count;
    }

    // The dates of the list are approximate, so a video is new when it is above the first video of the last list.
    // A video which comes back lower in the list does not count.
    private static int CountNewVideosInternal(MusicServiceUpload[] videos, HashSet<string> seen)
    {
        for (var i = 0; i < videos.Length; i++)
        {
            if (seen.Contains(videos[i].Id!))
                return i;
        }

        return videos.Length;
    }

    private static void AddVideoIdsInternal(HashSet<string> seen, MusicServiceUpload[] videos)
    {
        foreach (var video in videos)
            seen.Add(video.Id!);
    }

    public static EmbedBuilder BuildVideoEmbed(EmbedBuilder embed, MusicServiceUpload video, string rssUrl)
    {
        embed.WithFooter(rssUrl)
            .WithUrl(YT_WATCH_URL + video.Id)
            .WithTitle((string.IsNullOrWhiteSpace(video.Title) ? "-" : video.Title).TrimTo(256));

        if (Uri.TryCreate(video.Thumbnail, UriKind.Absolute, out var thumb)
            && (thumb.Scheme == Uri.UriSchemeHttps || thumb.Scheme == Uri.UriSchemeHttp))
            embed.WithImageUrl(video.Thumbnail);

        return embed;
    }

    // The channel of a YouTube upload feed which this bot reads from the music service, else null.
    private string? GetServiceChannelInternal(string key, string url)
    {
        if (!_serviceFeeds)
            return null;

        ref var channelId = ref CollectionsMarshal.GetValueRefOrAddDefault(_ytFeedChannels, key, out var exists);
        if (!exists)
            channelId = TryGetYtFeedChannelId(url, out var id) ? id : null;

        return channelId;
    }

    public static bool TryGetYtFeedChannelId(string url, [NotNullWhen(true)] out string? channelId)
    {
        channelId = null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || uri.Host is not ("youtube.com" or "www.youtube.com" or "m.youtube.com")
            || !uri.AbsolutePath.Equals(YT_FEED_PATH, StringComparison.OrdinalIgnoreCase))
            return false;

        var match = YtFeedChannelIdRegex().Match(uri.Query);
        if (!match.Success || !MusicServiceClient.IsChannelId(match.Groups["id"].ValueSpan))
            return false;

        channelId = match.Groups["id"].Value;
        return true;
    }

    // Reads a feed once before the bot adds it, from the music service for a YouTube upload feed when it can.
    public async Task<FeedCheckResult> CheckNewFeedAsync(string url)
    {
        if (music.IsConfigured && TryGetYtFeedChannelId(url, out var channelId))
        {
            return (await music.GetUploadsAsync(channelId)).Status switch
            {
                MusicServiceStatus.Ok => FeedCheckResult.Ok,
                MusicServiceStatus.NotFound => FeedCheckResult.Unreadable,
                _ => FeedCheckResult.Unavailable
            };
        }

        return (await FetchFeedAsync(url)).Feed is null ? FeedCheckResult.Unreadable : FeedCheckResult.Ok;
    }

    private async Task OnFeedFailureInternalAsync(string key, List<FeedSub> subs, int status, TimeSpan? retryAfter)
    {
        var failure = _health.OnFailure(key, status, retryAfter);
        if (failure.StreakStarted)
            MarkStreakInternal(subs, _streakStarts, _streakEnds);

        if (failure.Remove)
            await RemoveDeadFeedInternalAsync(key, subs, failure.Streak);
    }

    // Queues the embed for each sub, and collects the subs of servers which the bot left.
    private void QueuePostInternal(List<FeedSub> subs, EmbedBuilder embed, List<FeedSub> deadSubs)
    {
        foreach (var val in subs)
        {
            var guild = client.GetGuild(val.GuildId);
            if (guild is null)
            {
                if (!deadSubs.Contains(val))
                    deadSubs.Add(val);
                continue;
            }

            var ch = guild.GetTextChannel(val.ChannelId);
            if (ch is null || !CanPostIn(guild.CurrentUser, ch))
                continue;

            _sendTasks.Add(SendFeedItemInternalAsync(ch, val, embed));
        }
    }

    private async Task RemoveLeftGuildSubsInternalAsync(string key, string rssUrl, List<FeedSub> deadSubs)
    {
        if (deadSubs.Count == 0)
            return;

        Log.Debug(
            "Removing {Count} feed subscription(s) for {FeedUrl} - bot is no longer in those guilds: {GuildIds}",
            deadSubs.Count,
            rssUrl,
            string.Join(", ", deadSubs.Select(s => s.GuildId)));

        var deadIds = deadSubs.Select(s => s.Id).ToHashSet();
        await using var ctx = db.GetDbContext();
        await ctx.GetTable<FeedSub>()
            .Where(x => deadIds.Contains(x.Id))
            .DeleteAsync();

        _subs.AddOrUpdate(key,
            [],
            (_, old) => old.Where(x => !deadIds.Contains(x.Id)).ToList());
    }

    private static void MarkStreakInternal(List<FeedSub> subs, HashSet<int> add, HashSet<int> remove)
    {
        foreach (var sub in subs)
        {
            add.Add(sub.Id);
            remove.Remove(sub.Id);
        }
    }

    // Clears both sets once they are saved; on a database error they stay for the next tick.
    public static async Task SaveStreaksAsync(DbService db, HashSet<int> started, HashSet<int> ended, DateTime now)
    {
        if (started.Count == 0 && ended.Count == 0)
            return;

        await using var ctx = db.GetDbContext();
        if (started.Count > 0)
        {
            await ctx.GetTable<FeedSub>()
                .Where(x => started.Contains(x.Id) && x.FailingSince == null)
                .Set(x => x.FailingSince, (DateTime?)now)
                .UpdateAsync();

            started.Clear();
        }

        if (ended.Count > 0)
        {
            await ctx.GetTable<FeedSub>()
                .Where(x => ended.Contains(x.Id))
                .Set(x => x.FailingSince, (DateTime?)null)
                .UpdateAsync();

            ended.Clear();
        }
    }

    private async Task RemoveDeadFeedInternalAsync(string key, List<FeedSub> subs, TimeSpan streak)
    {
        var ids = subs.Select(x => x.Id).ToHashSet();

        // The guild/channel list lets the owner add the feed back from the logs after a wrong removal.
        Log.Warning("Removing feed {FeedUrl} from {Count} channel(s): it has failed for {Streak}. Channels: {Channels}",
            subs[0].Url,
            subs.Count,
            streak,
            string.Join(", ", subs.Select(static x => $"{x.GuildId}/{x.ChannelId}")));

        await using (var ctx = db.GetDbContext())
        {
            await ctx.GetTable<FeedSub>()
                .Where(x => ids.Contains(x.Id))
                .DeleteAsync();
        }

        _subs.AddOrUpdate(key,
            [],
            (_, old) => old.Where(x => !ids.Contains(x.Id)).ToList());

        _health.Forget(key);
        _lastPosts.TryRemove(key, out _);
        _seenVideos.Remove(key);
        _ytFeedChannels.Remove(key);
        foreach (var id in ids)
        {
            _streakStarts.Remove(id);
            _streakEnds.Remove(id);
        }

        foreach (var sub in subs)
        {
            try
            {
                var guild = client.GetGuild(sub.GuildId);
                var ch = guild?.GetTextChannel(sub.ChannelId);
                if (guild is null || ch is null || !CanPostIn(guild.CurrentUser, ch))
                    continue;

                await sender.Response(ch)
                    .Error(strs.feed_auto_removed(sub.Url))
                    .SendAsync();
            }
            catch (Exception ex)
            {
                Log.Warning("Unable to report the removed feed {FeedUrl} in {GuildId}/{ChannelId}: {ErrorMessage}",
                    sub.Url,
                    sub.GuildId,
                    sub.ChannelId,
                    ex.Message);
            }
        }
    }

    public List<FeedSub> GetFeeds(ulong guildId)
    {
        using var uow = db.GetDbContext();

        return uow.GetTable<FeedSub>()
            .Where(x => x.GuildId == guildId)
            .OrderBy(x => x.Id)
            .ToList();
    }

    public async Task<FeedFetchResult> FetchFeedAsync(string url)
    {
        if (!UrlExtensions.IsPublicUrl(url))
            return default;

        using var http = httpFactory.CreateClient(ExternalHttp.CLIENT_NAME);
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", USER_AGENT);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(FEED_TIMEOUT_SECONDS));
        try
        {
            using var res = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            var status = (int)res.StatusCode;
            var now = _time.GetUtcNow();
            if (!res.IsSuccessStatusCode)
                return new(null, status, null, FeedHealth.GetRetryAfter(res, now));

            if (await ExternalHttp.ReadLimitedAsync(res.Content, MAX_FEED_BYTES, cts.Token) is not { } data)
                return new(null, status, null, null);

            try
            {
                var feed = FeedReader.ReadFromByteArray(data.Buffer.AsSpan(0, data.Length).ToArray());
                return new(feed, status, FeedHealth.GetFreshness(res, now), null);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Unable to parse feed {FeedUrl}", url);
                return new(null, status, null, null);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(data.Buffer);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Unable to read feed {FeedUrl}", url);
            return default;
        }
    }

    public int MaxFeeds
        => scs.Data.MaxFeeds;

    public async Task<List<FeedSub>> GetFeedsAsync(ulong guildId)
    {
        await using var uow = db.GetDbContext();
        return await uow.GetTable<FeedSub>()
            .Where(x => x.GuildId == guildId)
            .OrderBy(x => x.Id)
            .ToListAsyncLinqToDB();
    }

    // The subscriptions which the feed loop of this shard posts to.
    public IReadOnlyList<FeedSub> GetTrackedSubs(string url)
        => _subs.TryGetValue(url.ToLower(), out var subs) ? subs : [];

    public async Task<(FeedAddResult Result, FeedSub? Sub)> AddFeedAsync(
        ulong guildId,
        ulong channelId,
        string rssFeed,
        string? message)
    {
        ArgumentNullException.ThrowIfNull(rssFeed, nameof(rssFeed));

        var feedUrl = rssFeed.Trim();

        if (!UrlExtensions.IsPublicUrl(feedUrl))
            return (FeedAddResult.Invalid, null);

        var max = scs.Data.MaxFeeds;
        await using var uow = db.GetDbContext();
        await using var lctx = uow.CreateLinqToDBContext();
        var table = lctx.GetTable<FeedSub>();

        // the duplicate check, the server limit and the insert are one statement, so parallel adds cannot pass them
        var ids = await lctx.SelectQuery(() => 1)
            .Where(_ => !table.Any(x => x.GuildId == guildId && x.Url.ToLower() == feedUrl.ToLower())
                        && table.Count(x => x.GuildId == guildId) < max)
            .InsertWithOutputAsync(table,
                _ => new FeedSub
                {
                    GuildId = guildId,
                    ChannelId = channelId,
                    Url = feedUrl,
                    Message = message
                },
                x => x.Id);

        if (ids.Length == 0)
        {
            var duplicate = await table.AnyAsyncLinqToDB(x => x.GuildId == guildId
                                                              && x.Url.ToLower() == feedUrl.ToLower());
            return (duplicate ? FeedAddResult.Duplicate : FeedAddResult.LimitReached, null);
        }

        var fs = new FeedSub
        {
            Id = ids[0],
            GuildId = guildId,
            ChannelId = channelId,
            Url = feedUrl,
            Message = message
        };

        _subs.AddOrUpdate(fs.Url.ToLower(),
            [fs],
            (_, old) => old.Append(fs).ToList());

        return (FeedAddResult.Success, fs);
    }

    // Changes only the given fields. The feed loop reads the new row from the next check on.
    public async Task<FeedSub?> UpdateFeedAsync(ulong guildId, int id, ulong? channelId, bool setMessage, string? message)
    {
        await using var uow = db.GetDbContext();
        var feeds = uow.GetTable<FeedSub>().Where(x => x.Id == id && x.GuildId == guildId);
        if (channelId is null && !setMessage)
            return await feeds.FirstOrDefaultAsyncLinqToDB();

        var update = feeds.AsUpdatable();
        if (channelId is { } newChannel)
            update = update.Set(x => x.ChannelId, newChannel);
        if (setMessage)
        {
            var newMessage = string.IsNullOrEmpty(message) ? null : message;
            update = update.Set(x => x.Message, newMessage);
        }

        var rows = await update.UpdateWithOutputAsync(static (_, inserted) => inserted);
        if (rows.Length == 0)
            return null;

        var updated = rows[0];
        var key = updated.Url.ToLower();

        // Only replaces a tracked entry, so an update which races a removal cannot track the removed feed again.
        while (_subs.TryGetValue(key, out var old))
        {
            if (ReplaceSubInternal(old, updated) is not { } next || _subs.TryUpdate(key, next, old))
                break;
        }

        return updated;
    }

    public async Task<bool> RemoveFeedAsync(ulong guildId, int id)
    {
        await using var uow = db.GetDbContext();
        var deleted = await uow.GetTable<FeedSub>()
            .Where(x => x.Id == id && x.GuildId == guildId)
            .DeleteWithOutputAsync();

        if (deleted.Length == 0)
            return false;

        var removedId = deleted[0].Id;
        _subs.AddOrUpdate(deleted[0].Url.ToLower(),
            [],
            (_, old) => old.Where(x => x.Id != removedId).ToList());

        return true;
    }

    private static List<FeedSub>? ReplaceSubInternal(List<FeedSub> subs, FeedSub updated)
    {
        for (var i = 0; i < subs.Count; i++)
        {
            if (subs[i].Id != updated.Id)
                continue;

            var next = new List<FeedSub>(subs);
            next[i] = updated;
            return next;
        }

        return null;
    }

    public bool RemoveFeed(ulong guildId, int index)
    {
        if (index < 0)
            return false;

        using var uow = db.GetDbContext();
        var items = uow.Set<FeedSub>()
            .Where(x => x.GuildId == guildId)
            .OrderBy(x => x.Id)
            .ToList();

        if (items.Count <= index)
            return false;

        var toRemove = items[index];
        _subs.AddOrUpdate(toRemove.Url.ToLower(),
            [],
            (_, old) => { return old.Where(x => x.Id != toRemove.Id).ToList(); });
        uow.Remove(toRemove);
        uow.SaveChanges();

        return true;
    }

    public static bool CanPostIn(IGuildUser bot, IGuildChannel channel)
    {
        var perms = bot.GetPermissions(channel);
        return perms.ViewChannel && perms.SendMessages && perms.EmbedLinks;
    }

    private async Task SendFeedItemInternalAsync(ITextChannel ch, FeedSub sub, EmbedBuilder embed)
    {
        try
        {
            await sender.Response(ch)
                .Embed(embed)
                .Text(string.IsNullOrWhiteSpace(sub.Message) ? string.Empty : sub.Message)
                .Mentions(TemplateMentions.FromTemplate(sub.Message))
                .SendAsync();
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
        {
            // Each 403 counts toward Discord's invalid request limit, which all shards share.
            Log.Warning("Removing feed {FeedUrl} from {GuildId}/{ChannelId}: the bot cannot post there",
                sub.Url,
                sub.GuildId,
                sub.ChannelId);

            await using var ctx = db.GetDbContext();
            await ctx.GetTable<FeedSub>().DeleteAsync(x => x.Id == sub.Id);
            _subs.AddOrUpdate(sub.Url.ToLower(),
                [],
                (_, old) => old.Where(x => x.Id != sub.Id).ToList());
        }
        catch (Exception ex)
        {
            Log.Warning("Unable to post feed {FeedUrl} to {GuildId}/{ChannelId}: {ErrorMessage}",
                sub.Url,
                sub.GuildId,
                sub.ChannelId,
                ex.Message);
        }
    }

    private const int YT_RESOLVE_TIMEOUT_SECONDS = 10;

    [GeneratedRegex(@"(?:^|[?&])channel_id=(?<id>[^&#]+)")]
    private static partial Regex YtFeedChannelIdRegex();

    [GeneratedRegex(@"youtube\.com/channel/(?<id>UC[\w-]+)")]
    private static partial Regex YtChannelUrlRegex();

    [GeneratedRegex(@"youtube\.com/@(?<handle>[^/?#\s]+)")]
    private static partial Regex YtHandleUrlRegex();

    [GeneratedRegex(@"youtube\.com/c/(?<name>[^/?#\s]+)")]
    private static partial Regex YtCustomUrlRegex();

    [GeneratedRegex(@"youtube\.com/user/(?<name>[^/?#\s]+)")]
    private static partial Regex YtUserUrlRegex();

    [GeneratedRegex(@"^@?[A-Za-z0-9._-]{3,30}$")]
    private static partial Regex YtBareHandleRegex();

    public static string YtFeedUrl(string channelId)
        => $"https://www.youtube.com/feeds/videos.xml?channel_id={channelId}";

    private static readonly string[] _ytChannelPaths = ["/channel/", "/@", "/c/", "/user/"];

    // A channel page link, not a feed link: the bot turns it into the upload feed of the channel.
    public static bool IsYtChannelLink(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Host is not ("youtube.com" or "www.youtube.com" or "m.youtube.com"))
            return false;

        var path = uri.AbsolutePath;
        foreach (var prefix in _ytChannelPaths)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    // The upload feed of a YouTube channel link, the same link for other feeds, null when the channel is not found.
    // Unavailable is true when the music service could not answer.
    public async Task<(string? Url, bool Unavailable)> ResolveFeedUrlAsync(string url)
    {
        if (!IsYtChannelLink(url))
            return (url, false);

        var (channelId, unavailable) = await ResolveYtChannelIdAsync(url);
        return (channelId is null ? null : YtFeedUrl(channelId), unavailable);
    }

    public async Task<(string? ChannelId, bool Unavailable)> ResolveYtChannelIdAsync(string input)
    {
        var direct = YtChannelUrlRegex().Match(input);
        if (direct.Success)
            return (direct.Groups["id"].Value, false);

        var trimmed = input.Trim();
        if (MusicServiceClient.IsChannelId(trimmed))
            return (trimmed, false);

        if (music.IsConfigured)
            return await ResolveThroughServiceInternalAsync(trimmed);

        var pageUrl = BuildYtPageUrl(input);
        if (pageUrl is null)
            return (null, false);

        try
        {
            using var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(YT_RESOLVE_TIMEOUT_SECONDS);
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", USER_AGENT);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(YT_RESOLVE_TIMEOUT_SECONDS));
            using var response = await http.GetAsync(
                pageUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cts.Token);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            return (await ScanForChannelIdAsync(stream, cts.Token), false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to resolve youtube channel id from {Input}", input);
            return (null, false);
        }
    }

    // YouTube refuses its pages to many hosts, so a bot which uses the music service asks the service.
    private async Task<(string? ChannelId, bool Unavailable)> ResolveThroughServiceInternalAsync(string input)
    {
        if (input.Length == 0)
            return (null, false);

        var query = input[0] != '@' && YtBareHandleRegex().IsMatch(input) ? "@" + input : input;
        var res = await music.ResolveChannelAsync(query);
        return res.Status switch
        {
            MusicServiceStatus.Ok => (res.Channel?.Id, false),
            MusicServiceStatus.Failed => (null, true),
            _ => (null, false)
        };
    }

    private static ReadOnlySpan<byte> CanonicalAnchor
        => "<link rel=\"canonical\" href=\"https://www.youtube.com/channel/"u8;

    private static ReadOnlySpan<byte> ExternalIdAnchor
        => "\"externalId\":\""u8;

    public static async Task<string?> ScanForChannelIdAsync(Stream stream, CancellationToken ct)
    {
        const int chunk = 64 * 1024;
        const int carry = 256;
        const long maxBytes = 4 * 1024 * 1024;

        var buffer = ArrayPool<byte>.Shared.Rent(chunk + carry);
        try
        {
            var kept = 0;
            long total = 0;
            while (total < maxBytes)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(kept, chunk), ct);
                if (read == 0)
                    break;

                total += read;
                var span = buffer.AsSpan(0, kept + read);

                if (TryFindChannelId(span, out var id))
                    return id;

                kept = Math.Min(carry, span.Length);
                span[^kept..].CopyTo(buffer);
            }

            return null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool TryFindChannelId(ReadOnlySpan<byte> span, out string? id)
    {
        if (TryExtractAfter(span, CanonicalAnchor, out id))
            return true;

        return TryExtractAfter(span, ExternalIdAnchor, out id);
    }

    private static bool TryExtractAfter(ReadOnlySpan<byte> span, ReadOnlySpan<byte> anchor, out string? id)
    {
        id = null;
        var searchStart = 0;

        while (true)
        {
            var rel = span[searchStart..].IndexOf(anchor);
            if (rel < 0)
                return false;

            var valueStart = searchStart + rel + anchor.Length;
            var rest = span[valueStart..];

            if (rest.Length < 2 || rest[0] != (byte)'U' || rest[1] != (byte)'C')
            {
                searchStart = valueStart;
                continue;
            }

            var end = rest.IndexOf((byte)'"');
            if (end < 0)
                return false;

            var value = rest[..end];
            if (IsValidChannelId(value))
            {
                id = System.Text.Encoding.ASCII.GetString(value);
                return true;
            }

            searchStart = valueStart;
        }
    }

    private static bool IsValidChannelId(ReadOnlySpan<byte> value)
    {
        if (value.Length is < 3 or > 64)
            return false;

        foreach (var b in value)
        {
            var ok = b is >= (byte)'A' and <= (byte)'Z'
                     or >= (byte)'a' and <= (byte)'z'
                     or >= (byte)'0' and <= (byte)'9'
                     or (byte)'_'
                     or (byte)'-';
            if (!ok)
                return false;
        }

        return true;
    }

    private static string? BuildYtPageUrl(string input)
    {
        var handleMatch = YtHandleUrlRegex().Match(input);
        if (handleMatch.Success)
            return $"https://www.youtube.com/@{Uri.EscapeDataString(handleMatch.Groups["handle"].Value)}";

        var customMatch = YtCustomUrlRegex().Match(input);
        if (customMatch.Success)
            return $"https://www.youtube.com/c/{Uri.EscapeDataString(customMatch.Groups["name"].Value)}";

        var userMatch = YtUserUrlRegex().Match(input);
        if (userMatch.Success)
            return $"https://www.youtube.com/user/{Uri.EscapeDataString(userMatch.Groups["name"].Value)}";

        var trimmed = input.Trim();
        if (YtBareHandleRegex().IsMatch(trimmed))
        {
            var handle = trimmed[0] == '@' ? trimmed[1..] : trimmed;
            return $"https://www.youtube.com/@{Uri.EscapeDataString(handle)}";
        }

        return null;
    }
}

public enum FeedAddResult
{
    Success,
    LimitReached,
    Invalid,
    Duplicate,
}
