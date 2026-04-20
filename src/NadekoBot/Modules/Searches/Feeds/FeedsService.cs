#nullable disable
using CodeHollow.FeedReader;
using CodeHollow.FeedReader.Feeds;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Searches.Services;

public class FeedsService : INService, IReadyExecutor
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

    private readonly NonBlocking.ConcurrentDictionary<string, DateTime> _lastPosts = new();
    private readonly Dictionary<string, uint> _errorCounters = new();

    public FeedsService(
        DbService db,
        DiscordSocketClient client,
        IMessageSenderService sender,
        ShardData shardData,
        SearchesConfigService scs)
    {
        _db = db;
        _client = client;
        _sender = sender;
        _shardData = shardData;
        _scs = scs;
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
}

public enum FeedAddResult
{
    Success,
    LimitReached,
    Invalid,
    Duplicate,
}