#nullable disable
using CodeHollow.FeedReader;
using CodeHollow.FeedReader.Feeds;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using System.Collections.Concurrent;

namespace NadekoBot.Modules.Searches.Services;

public class FeedsService : INService, IReadyExecutor
{
    private readonly DbService _db;
    private NonBlocking.ConcurrentDictionary<string, List<FeedSub>> _subs;
    private readonly DiscordSocketClient _client;
    private readonly IMessageSenderService _sender;
    private readonly ShardData _shardData;

    private readonly NonBlocking.ConcurrentDictionary<string, DateTime> _lastPosts = new();
    private readonly Dictionary<string, uint> _errorCounters = new();

    public FeedsService(
        DbService db,
        DiscordSocketClient client,
        IMessageSenderService sender,
        ShardData shardData)
    {
        _db = db;


        _client = client;
        _sender = sender;
        _shardData = shardData;
    }

    public async Task OnReadyAsync()
    {
        await using (var uow = _db.GetDbContext())
        {
            var subs = await uow.Set<FeedSub>()
                                .AsQueryable()
                                .Where(x => Queries.GuildOnShard(x.GuildId, _shardData.TotalShards, _shardData.ShardId))
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

    private async Task<uint> AddError(string url, List<int> ids)
    {
        try
        {
            var newValue = _errorCounters[url] = _errorCounters.GetValueOrDefault(url) + 1;

            if (newValue >= 100)
            {
                // remove from db
                await using var ctx = _db.GetDbContext();
                await ctx.GetTable<FeedSub>()
                         .DeleteAsync(x => ids.Contains(x.Id));

                // remove from the local cache
                _subs.TryRemove(url, out _);

                // reset the error counter
                ClearErrors(url);
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

    public async Task<EmbedBuilder> TrackFeeds()
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
                    var feed = await FeedReader.ReadAsync(rssUrl);

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

                    for (var index = 1; index <= items.Count; index++)
                    {
                        var (feedItem, itemUpdateDate) = items[^index];
                        if (itemUpdateDate <= lastFeedUpdate)
                            continue;

                        var embed = _sender.CreateEmbed().WithFooter(rssUrl);

                        _lastPosts[kvp.Key] = itemUpdateDate;

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
                            var previewElement = afi.Element.Elements()
                                                    .FirstOrDefault(x => x.Name.LocalName == "preview");

                            if (previewElement is null)
                            {
                                previewElement = afi.Element.Elements()
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
                                    gotImage = true;
                                }
                            }
                        }

                        embed.WithTitle(title.TrimTo(256));

                        var desc = feedItem.Description?.StripHtml();
                        if (!string.IsNullOrWhiteSpace(feedItem.Description))
                            embed.WithDescription(desc.TrimTo(2048));


                        var tasks = new List<Task>();

                        foreach (var val in kvp.Value)
                        {
                            var ch = _client.GetGuild(val.GuildId).GetTextChannel(val.ChannelId);

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

                        allSendTasks.Add(tasks.WhenAll());

                        // as data retrieval was successful, reset error counter
                        ClearErrors(rssUrl);
                    }
                }
                catch (Exception ex)
                {
                    var errorCount = await AddError(rssUrl, kvp.Value.Select(x => x.Id).ToList());

                    Log.Warning("An error occured while getting rss stream ({ErrorCount} / 100) {RssFeed}"
                                + "\n {Message}",
                        errorCount,
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
        
    public FeedAddResult AddFeed(
        ulong guildId,
        ulong channelId,
        string rssFeed,
        string message)
    {
        ArgumentNullException.ThrowIfNull(rssFeed, nameof(rssFeed));

        var fs = new FeedSub
        {
            ChannelId = channelId,
            Url = rssFeed.Trim(),
            Message = message
        };

        using var uow = _db.GetDbContext();
        var feeds = uow.GetTable<FeedSub>()
                       .Where(x => x.GuildId == guildId)
                       .ToArray();

        if (feeds.Any(x => x.Url.ToLower() == fs.Url.ToLower()))
            return FeedAddResult.Duplicate;
        if (feeds.Length >= 10)
            return FeedAddResult.LimitReached;

        uow.Add(fs);
        uow.SaveChanges();
        
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
            (_, old) =>
            {
                return old.Where(x => x.Id != toRemove.Id).ToList();
            });
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