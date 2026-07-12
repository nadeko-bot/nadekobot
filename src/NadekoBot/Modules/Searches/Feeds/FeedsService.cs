#nullable disable
using CodeHollow.FeedReader;
using CodeHollow.FeedReader.Feeds;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using System.Buffers;
using System.Text.RegularExpressions;

namespace NadekoBot.Modules.Searches.Services;

public sealed partial class FeedsService : INService, IReadyExecutor
{
    public const string USER_AGENT =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/139.0.0.0 Safari/537.36 OPR/123.0.0.0 (Edition beta)";

    private const int MAX_FEED_ERRORS = 500;

    private readonly DbService _db;
    private NonBlocking.ConcurrentDictionary<string, List<FeedSub>> _subs;
    private readonly DiscordSocketClient _client;
    private readonly IMessageSenderService _sender;
    private readonly ShardData _shardData;
    private readonly SearchesConfigService _scs;
    private readonly IHttpClientFactory _httpFactory;

    private readonly NonBlocking.ConcurrentDictionary<string, DateTime> _lastPosts = new();
    private readonly Dictionary<string, uint> _errorCounters = new();

    public FeedsService(
        DbService db,
        DiscordSocketClient client,
        IMessageSenderService sender,
        ShardData shardData,
        SearchesConfigService scs,
        IHttpClientFactory httpFactory)
    {
        _db = db;
        _client = client;
        _sender = sender;
        _shardData = shardData;
        _scs = scs;
        _httpFactory = httpFactory;
    }

    public async Task OnReadyAsync()
    {
        await using (var uow = _db.GetDbContext())
        {
            var subs = await uow.Set<FeedSub>()
                .AsQueryable()
                .Where(Queries.GuildOnShard<FeedSub>(x => x.GuildId, _shardData.TotalShards, _shardData.ShardId))
                .ToListAsyncLinqToDB();
            _subs = subs
                .GroupBy(x => x.Url.ToLower())
                .ToDictionary(x => x.Key, x => x.ToList())
                .ToConcurrent();
        }

        await TrackFeeds();
    }

    private void ClearErrors(string url)
        => _errorCounters.Remove(url);

    private async Task<uint> AddError(string url, List<FeedSub> subs)
    {
        try
        {
            var newValue = _errorCounters[url] = _errorCounters.GetValueOrDefault(url) + 1;

            if (newValue >= MAX_FEED_ERRORS)
            {
                Log.Debug("Feed {FeedUrl} reached {MaxErrors} errors, removing {SubCount} subscription(s)",
                    url,
                    MAX_FEED_ERRORS,
                    subs.Count);

                await using var ctx = _db.GetDbContext();
                await ctx.GetTable<FeedSub>()
                    .DeleteAsync(x => subs.Select(s => s.Id).Contains(x.Id));

                _subs.TryRemove(url, out _);
                ClearErrors(url);

                foreach (var sub in subs)
                {
                    try
                    {
                        var guild = _client.GetGuild(sub.GuildId);
                        if (guild is null)
                            continue;

                        var ch = guild.GetTextChannel(sub.ChannelId);
                        if (ch is null)
                            continue;

                        await _sender.Response(ch)
                            .Error(strs.feed_auto_removed(url))
                            .SendAsync();
                    }
                    catch { }
                }
            }

            return newValue;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error adding rss errors...");
            return 0;
        }
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
        if (!string.IsNullOrWhiteSpace(feedItem.Description))
            embed.WithDescription(desc.TrimTo(2048));

        return embed;
    }

    private async Task TrackFeeds()
    {
        while (true)
        {
            var allSendTasks = new List<Task>(_subs.Count);
            foreach (var kvp in _subs)
            {
                if (kvp.Value.Count == 0)
                    continue;

                var rssUrl = kvp.Value.First().Url;
                try
                {
                    var feedTask = FeedReader.ReadAsync(rssUrl, userAgent: USER_AGENT);
                    var completed = await Task.WhenAny(feedTask, Task.Delay(TimeSpan.FromSeconds(15)));
                    if (completed != feedTask)
                    {
                        Log.Debug("Feed {FeedUrl} timed out after 15 seconds", rssUrl);
                        await AddError(rssUrl, kvp.Value);
                        continue;
                    }

                    var feed = await feedTask;

                    var items = new List<(FeedItem Item, DateTime LastUpdate)>();
                    foreach (var item in feed.Items)
                    {
                        var pubDate = GetPubDate(item);

                        if (pubDate is null)
                            continue;

                        items.Add((item, pubDate.Value.ToUniversalTime()));

                        // show at most 3 items if you're behind
                        if (items.Count > 2)
                            break;
                    }

                    if (items.Count == 0)
                        continue;

                    if (!_lastPosts.TryGetValue(kvp.Key, out var lastFeedUpdate))
                    {
                        lastFeedUpdate = _lastPosts[kvp.Key] = items[0].LastUpdate;
                    }

                    var deadSubs = new List<FeedSub>();

                    for (var index = 1; index <= items.Count; index++)
                    {
                        var (feedItem, itemUpdateDate) = items[^index];
                        if (itemUpdateDate <= lastFeedUpdate)
                            continue;

                        var embed = BuildFeedEmbed(_sender.CreateEmbed(), feedItem, rssUrl);

                        _lastPosts[kvp.Key] = itemUpdateDate;

                        var tasks = new List<Task>();

                        foreach (var val in kvp.Value)
                        {
                            try
                            {
                                var guild = _client.GetGuild(val.GuildId);
                                if (guild is null)
                                {
                                    deadSubs.Add(val);
                                    continue;
                                }

                                var ch = guild.GetTextChannel(val.ChannelId);
                                if (ch is null)
                                    continue;

                                var sendTask = _sender.Response(ch)
                                    .Embed(embed)
                                    .Text(string.IsNullOrWhiteSpace(val.Message)
                                        ? string.Empty
                                        : val.Message)
                                    .Sanitize(false)
                                    .SendAsync();
                                tasks.Add(sendTask);
                            }
                            catch (Exception ex)
                            {
                                Log.Debug(ex,
                                    "Error sending feed update to {GuildId}/{ChannelId}",
                                    val.GuildId,
                                    val.ChannelId);
                            }
                        }

                        allSendTasks.Add(tasks.WhenAll());
                    }

                    if (deadSubs.Count > 0)
                    {
                        Log.Debug(
                            "Removing {Count} feed subscription(s) for {FeedUrl} - bot is no longer in those guilds: {GuildIds}",
                            deadSubs.Count,
                            rssUrl,
                            string.Join(", ", deadSubs.Select(s => s.GuildId)));

                        await using var ctx = _db.GetDbContext();
                        await ctx.GetTable<FeedSub>()
                            .DeleteAsync(x => deadSubs.Select(s => s.Id).Contains(x.Id));

                        var deadIds = deadSubs.Select(s => s.Id).ToHashSet();
                        _subs.AddOrUpdate(kvp.Key,
                            [],
                            (_, old) => old.Where(x => !deadIds.Contains(x.Id)).ToList());
                    }

                    ClearErrors(rssUrl);
                }
                catch (Exception ex)
                {
                    var errorCount = await AddError(rssUrl, kvp.Value);

                    Log.Debug("An error occured while getting rss stream ({ErrorCount} / {MaxErrors}) {RssFeed}"
                                + "\n {Message}",
                        errorCount,
                        MAX_FEED_ERRORS,
                        rssUrl,
                        $"[{ex.GetType().Name}]: {ex.Message}");
                }
            }

            await Task.WhenAll(Task.WhenAll(allSendTasks), Task.Delay(30000));
        }
    }

    public List<FeedSub> GetFeeds(ulong guildId)
    {
        using var uow = _db.GetDbContext();

        return uow.GetTable<FeedSub>()
            .Where(x => x.GuildId == guildId)
            .OrderBy(x => x.Id)
            .ToList();
    }

    public async Task<FeedAddResult> AddFeedAsync(
        ulong guildId,
        ulong channelId,
        string rssFeed,
        string message)
    {
        ArgumentNullException.ThrowIfNull(rssFeed, nameof(rssFeed));

        var feedUrl = rssFeed.Trim();

        if (!UrlExtensions.IsPublicUrl(feedUrl))
            return FeedAddResult.Invalid;

        await using var uow = _db.GetDbContext();
        if (await uow.GetTable<FeedSub>().AnyAsyncLinqToDB(x => x.GuildId == guildId &&
                                                                x.Url.ToLower() == feedUrl.ToLower()))
            return FeedAddResult.Duplicate;

        var count = await uow.GetTable<FeedSub>().CountAsyncLinqToDB(x => x.GuildId == guildId);
        if (count >= _scs.Data.MaxFeeds)
            return FeedAddResult.LimitReached;

        var fs = await uow.GetTable<FeedSub>()
            .InsertWithOutputAsync(() => new FeedSub
            {
                GuildId = guildId,
                ChannelId = channelId,
                Url = feedUrl,
                Message = message
            });

        _subs.AddOrUpdate(fs.Url.ToLower(),
            [fs],
            (_, old) => old.Append(fs).ToList());

        return FeedAddResult.Success;
    }

    public bool RemoveFeed(ulong guildId, int index)
    {
        if (index < 0)
            return false;

        using var uow = _db.GetDbContext();
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

    private const int YT_RESOLVE_TIMEOUT_SECONDS = 10;

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

    public async Task<string?> ResolveYtChannelIdAsync(string input)
    {
        var direct = YtChannelUrlRegex().Match(input);
        if (direct.Success)
            return direct.Groups["id"].Value;

        var pageUrl = BuildYtPageUrl(input);
        if (pageUrl is null)
            return null;

        try
        {
            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(YT_RESOLVE_TIMEOUT_SECONDS);
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", USER_AGENT);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(YT_RESOLVE_TIMEOUT_SECONDS));
            using var response = await http.GetAsync(
                pageUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cts.Token);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            return await ScanForChannelIdAsync(stream, cts.Token);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to resolve youtube channel id from {Input}", input);
            return null;
        }
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