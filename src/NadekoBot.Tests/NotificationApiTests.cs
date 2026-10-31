#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Time.Testing;
using Nadeko.Common;
using NadekoBot.Common;
using NadekoBot.Common.Configs;
using NadekoBot.Db.Models;
using NadekoBot.Extensions;
using NadekoBot.Hosted;
using NadekoBot.Hosted.Endpoints;
using NadekoBot.Modules.Music;
using NadekoBot.Modules.Searches;
using NadekoBot.Modules.Searches.Common;
using NadekoBot.Modules.Searches.Common.StreamNotifications;
using NadekoBot.Modules.Searches.Common.StreamNotifications.Providers;
using NadekoBot.Modules.Searches.Services;
using NadekoBot.Services;
using NadekoBot.Tests.Waifu;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests;

// The feed and stream routes of the dashboard, and YouTube live through the music service.
[TestFixture]
public sealed class NotificationApiTests
{
    private const string BASE_URL = "http://10.0.0.5:8080";
    private const string KEY = "nadeko-public.test-signature";
    private const ulong GUILD = 1;
    private const ulong OTHER_GUILD = 2;
    private const ulong CHANNEL = 10;
    private const ulong OTHER_CHANNEL = 11;
    private const ulong THIRD_CHANNEL = 12;
    private const int MAX_FEEDS = 2;
    private const string VIDEO_ID = "dQw4w9WgXcQ";

    private static readonly string _live = ChannelId('l');
    private static readonly string _offline = ChannelId('o');
    private static readonly string _unknown = ChannelId('u');
    private static readonly string _missing = ChannelId('m');

    private sealed class FakeServiceHandler : HttpMessageHandler
    {
        public readonly List<(HttpMethod Method, string PathAndQuery, string? Body)> Requests = [];
        public Func<HttpRequestMessage, string?, HttpResponseMessage>? Respond;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            lock (Requests)
                Requests.Add((request.Method, request.RequestUri!.PathAndQuery, body));

            return Respond!(request, body);
        }
    }

    private TestDbService _db = null!;
    private FakeServiceHandler _handler = null!;
    private IHttpClientFactory _factory = null!;
    private SearchesConfigService _config = null!;
    private MusicServiceClient _music = null!;
    private DiscordSocketClient _client = null!;
    private int _oldMaxFeeds;

    [SetUp]
    public void SetUp()
    {
        _db = new TestDbService();
        _handler = new FakeServiceHandler();
        _factory = Substitute.For<IHttpClientFactory>();
        _factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(_handler, false));

        _config = new SearchesConfigService(new YamlSeria(), Substitute.For<IPubSub>(), new BotPolicy(false));
        _oldMaxFeeds = _config.Data.MaxFeeds;
        _config.ModifyConfig(c =>
        {
            c.MusicServiceUrl = BASE_URL;
            c.MaxFeeds = MAX_FEEDS;
        });

        _music = new MusicServiceClient(_factory, _config, new BotPolicy(false)) { Key = KEY };
        // Not shard 0, so the service does not start the stream check loop.
        _client = new DiscordSocketClient(new DiscordSocketConfig { ShardId = 1, TotalShards = 2 });
    }

    [TearDown]
    public void TearDown()
    {
        _config.ModifyConfig(c =>
        {
            c.MusicServiceUrl = string.Empty;
            c.MaxFeeds = _oldMaxFeeds;
        });
        _client.Dispose();
        _handler.Dispose();
        _db.Dispose();
    }

    private static string ChannelId(char c)
        => "UC" + new string(c, 22);

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (int Status, object? Value) Read(IResult result)
        => ((result as IStatusCodeHttpResult)?.StatusCode ?? 200, (result as IValueHttpResult)?.Value);

    private static string? ErrorOf(IResult result)
        => (Read(result).Value as ErrorDto)?.Error;

    private FeedsService CreateFeeds()
        => new(_db,
            _client,
            Substitute.For<IMessageSenderService>(),
            new ShardData(_client, Substitute.For<IBotCreds>()),
            _config,
            _factory,
            _music);

    private StreamNotificationService CreateStreams(IBotCreds? creds = null)
        => new(_db,
            _client,
            Substitute.For<IBotStrings>(),
            Substitute.For<IBotCredsProvider>(),
            _factory,
            Substitute.For<IPubSub>(),
            Substitute.For<IMessageSenderService>(),
            _config,
            new ShardData(_client, creds ?? Substitute.For<IBotCreds>()),
            Substitute.For<IReplacementService>(),
            _music);

    [Test]
    public async Task Feeds_AddRefusesDuplicatesAndTheLimitAndEditsAndRemovesByIdWithTheLoopCache()
    {
        var feeds = CreateFeeds();
        const string first = "https://1.1.1.1/a.xml";

        var (added, sub) = await feeds.AddFeedAsync(GUILD, CHANNEL, first, "hi");
        Assert.That((added, sub?.Id > 0), Is.EqualTo((FeedAddResult.Success, true)));
        Assert.That((await feeds.AddFeedAsync(GUILD, CHANNEL, "https://1.1.1.1/A.XML", null)).Result, Is.EqualTo(FeedAddResult.Duplicate));
        Assert.That((await feeds.AddFeedAsync(GUILD, CHANNEL, "https://1.1.1.1/b.xml", null)).Result, Is.EqualTo(FeedAddResult.Success));
        Assert.That((await feeds.AddFeedAsync(GUILD, CHANNEL, "https://1.1.1.1/c.xml", null)).Result, Is.EqualTo(FeedAddResult.LimitReached));
        Assert.That((await feeds.AddFeedAsync(OTHER_GUILD, CHANNEL, first, null)).Result, Is.EqualTo(FeedAddResult.Success));

        var id = sub!.Id;
        var moved = await WebFeedEndpoints.UpdateFeedAsync(GUILD, id, OTHER_CHANNEL, false, null, feeds);
        var dto = (FeedSubDto)Read(moved).Value!;
        Assert.That((dto.ChannelId, dto.Message, dto.Url), Is.EqualTo((OTHER_CHANNEL.ToString(), "hi", first)));

        // The next post of the feed loop goes to the new channel.
        var tracked = feeds.GetTrackedSubs(first).Where(x => x.GuildId == GUILD).ToArray();
        Assert.That(tracked.Select(static x => (x.ChannelId, x.Message)), Is.EqualTo(new[] { (OTHER_CHANNEL, "hi") }));

        var cleared = (FeedSubDto)Read(await WebFeedEndpoints.UpdateFeedAsync(GUILD, id, null, true, null, feeds)).Value!;
        Assert.That((cleared.ChannelId, cleared.Message), Is.EqualTo((OTHER_CHANNEL.ToString(), (string?)null)));
        Assert.That(ErrorOf(await WebFeedEndpoints.UpdateFeedAsync(OTHER_GUILD, id, CHANNEL, false, null, feeds)), Is.EqualTo("feed_not_found"));

        Assert.That(await feeds.RemoveFeedAsync(OTHER_GUILD, id), Is.False);
        Assert.That(await feeds.RemoveFeedAsync(GUILD, id), Is.True);
        Assert.That(feeds.GetTrackedSubs(first).Select(static x => x.GuildId), Is.EqualTo(new[] { OTHER_GUILD }));
        Assert.That((await feeds.GetFeedsAsync(GUILD)).Select(static x => x.Url), Is.EqualTo(new[] { "https://1.1.1.1/b.xml" }));
    }

    [Test]
    public async Task Feeds_YouTubeChannelLinksBecomeTheUploadFeedThroughTheService()
    {
        var channel = ChannelId('c');
        _handler.Respond = (req, _) => req.RequestUri!.Query.Contains("missing", StringComparison.Ordinal)
            ? Json(HttpStatusCode.NotFound, """{"error":"unavailable"}""")
            : req.RequestUri!.Query.Contains("busy", StringComparison.Ordinal)
                ? Json(HttpStatusCode.ServiceUnavailable, """{"error":"no_exit"}""")
                : Json(HttpStatusCode.OK, $$"""{"id":"{{channel}}","name":"Nadeko","avatar":null}""");
        var feeds = CreateFeeds();

        Assert.That(await feeds.ResolveFeedUrlAsync("https://www.youtube.com/@nadeko"), Is.EqualTo((FeedsService.YtFeedUrl(channel), false)));
        Assert.That(await feeds.ResolveFeedUrlAsync("https://m.youtube.com/c/nadeko/videos"), Is.EqualTo((FeedsService.YtFeedUrl(channel), false)));
        Assert.That(await feeds.ResolveYtChannelIdAsync("nadeko"), Is.EqualTo((channel, false)));
        Assert.That(await feeds.ResolveFeedUrlAsync("https://www.youtube.com/@missing"), Is.EqualTo(((string?)null, false)));
        Assert.That(_handler.Requests.Select(static r => r.PathAndQuery),
            Is.EqualTo(new[]
            {
                "/v1/channels/resolve?url=https%3A%2F%2Fwww.youtube.com%2F%40nadeko",
                "/v1/channels/resolve?url=https%3A%2F%2Fm.youtube.com%2Fc%2Fnadeko%2Fvideos",
                "/v1/channels/resolve?url=%40nadeko",
                "/v1/channels/resolve?url=https%3A%2F%2Fwww.youtube.com%2F%40missing"
            }));

        // A channel id needs no lookup, and a feed link stays as it is.
        _handler.Requests.Clear();
        Assert.That(await feeds.ResolveFeedUrlAsync($"https://www.youtube.com/channel/{channel}/streams"), Is.EqualTo((FeedsService.YtFeedUrl(channel), false)));
        Assert.That(await feeds.ResolveFeedUrlAsync(FeedsService.YtFeedUrl(channel)), Is.EqualTo((FeedsService.YtFeedUrl(channel), false)));
        Assert.That(await feeds.ResolveFeedUrlAsync("https://example.com/feed.xml"), Is.EqualTo(("https://example.com/feed.xml", false)));
        Assert.That(_handler.Requests, Is.Empty);

        // Over its hourly limit the service answers 503 no_exit: the route says YouTube cannot be checked now.
        Assert.That(await feeds.ResolveFeedUrlAsync("https://www.youtube.com/@busy"), Is.EqualTo(((string?)null, true)));
        var busy = Read(await WebFeedEndpoints.AddFeedAsync(GUILD, CHANNEL, "https://www.youtube.com/@busy", null, feeds));
        Assert.That(busy.Status, Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
    }

    [Test]
    public async Task Feeds_YouTubeUploadsComeFromTheServiceAndOnlyAMissingChannelFails()
    {
        var channel = ChannelId('c');
        var feedUrl = FeedsService.YtFeedUrl(channel);
        (HttpStatusCode Status, string Body) answer = (HttpStatusCode.OK, "{}");
        _handler.Respond = (_, _) => Json(answer.Status, answer.Body);
        var feeds = CreateFeeds();

        answer = (HttpStatusCode.OK, $$"""
            {"id":"{{channel}}","name":"Nadeko","avatar":null,"videos":[
              {"id":"{{VIDEO_ID}}","title":"New","url":"https://www.youtube.com/watch?v={{VIDEO_ID}}","thumbnail":"https://i.ytimg.com/vi/{{VIDEO_ID}}/hqdefault.jpg","durationSec":60,"publishedAt":null},
              {"id":"not a video","title":"Bad","url":null,"thumbnail":null,"durationSec":0,"publishedAt":null}]}
            """);
        var ok = await _music.GetUploadsAsync(channel);
        Assert.That((ok.Status, ok.Videos.Select(static v => v.Id).ToArray()),
            Is.EqualTo((MusicServiceStatus.Ok, new[] { VIDEO_ID })));
        Assert.That(await feeds.CheckNewFeedAsync(feedUrl), Is.EqualTo(FeedCheckResult.Ok));
        Assert.That(Read(await WebFeedEndpoints.AddFeedAsync(GUILD, CHANNEL, feedUrl, null, feeds)).Status,
            Is.EqualTo(StatusCodes.Status201Created));
        Assert.That((await _music.GetUploadsAsync(ChannelId('d'))).Status, Is.EqualTo(MusicServiceStatus.Failed),
            "a list for another channel");

        // Only the service's "no such channel" fails the feed; an older service without the route answers not_found.
        answer = (HttpStatusCode.NotFound, """{"error":"unavailable"}""");
        Assert.That(await feeds.CheckNewFeedAsync(feedUrl), Is.EqualTo(FeedCheckResult.Unreadable));
        foreach (var (status, body) in new[]
                 {
                     (HttpStatusCode.NotFound, """{"error":"not_found"}"""),
                     (HttpStatusCode.ServiceUnavailable, """{"error":"no_exit"}""")
                 })
        {
            answer = (status, body);
            Assert.That((await _music.GetUploadsAsync(channel)).Status, Is.EqualTo(MusicServiceStatus.Failed));
            Assert.That(Read(await WebFeedEndpoints.AddFeedAsync(GUILD, OTHER_CHANNEL, feedUrl, null, feeds)).Status,
                Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
        }

        Assert.That(_handler.Requests.Select(static r => r.PathAndQuery).Distinct(),
            Is.EquivalentTo(new[] { $"/v1/channels/{channel}/uploads", $"/v1/channels/{ChannelId('d')}/uploads" }));

        Assert.That(FeedsService.TryGetYtFeedChannelId($"http://youtube.com/feeds/videos.xml?channel_id={channel}", out var id)
                    && id == channel);
        Assert.That(FeedsService.TryGetYtFeedChannelId($"https://www.youtube.com/feeds/videos.xml?x=1&channel_id={channel}", out _));
        Assert.That(FeedsService.TryGetYtFeedChannelId("https://www.youtube.com/feeds/videos.xml?user=nadeko", out _), Is.False);
        Assert.That(FeedsService.TryGetYtFeedChannelId($"https://example.com/feeds/videos.xml?channel_id={channel}", out _), Is.False);
    }

    [Test]
    public void Uploads_NewVideosAreFoundByIdAndAServiceOutageKeepsTheStreak()
    {
        static MusicServiceUpload[] List(params string[] ids)
            => ids.Select(static x => new MusicServiceUpload(x, x, null)).ToArray();

        // The first read posts nothing, also for a channel with no videos yet, whose first upload then counts.
        HashSet<string>? seen = null;
        Assert.That(FeedsService.TakeNewVideos(List("b", "a"), ref seen), Is.Zero);
        HashSet<string>? empty = null;
        Assert.That(FeedsService.TakeNewVideos([], ref empty), Is.Zero);
        Assert.That(FeedsService.TakeNewVideos(List("a"), ref empty), Is.EqualTo(1));

        Assert.That(FeedsService.TakeNewVideos(List("c", "b", "a"), ref seen), Is.EqualTo(1));
        // An empty answer keeps the known ids; "c" was then deleted and an older video moves into the list.
        Assert.That(FeedsService.TakeNewVideos([], ref seen), Is.Zero);
        Assert.That(FeedsService.TakeNewVideos(List("b", "a", "z"), ref seen), Is.Zero);
        // Five new videos: the three newest are posted, and all five are known after the read.
        Assert.That(FeedsService.TakeNewVideos(List("h", "g", "f", "e", "d", "b"), ref seen), Is.EqualTo(3));
        Assert.That(seen, Is.EquivalentTo(new[] { "h", "g", "f", "e", "d", "b" }));
        Assert.That(FeedsService.TakeNewVideos(List("y", "x"), ref seen), Is.EqualTo(2));

        var embed = FeedsService.BuildVideoEmbed(new EmbedBuilder(),
            new MusicServiceUpload(VIDEO_ID, "New", "https://i.ytimg.com/vi/x/hqdefault.jpg"),
            "rss");
        Assert.That((embed.Url, embed.Title, embed.ImageUrl, embed.Footer?.Text),
            Is.EqualTo(("https://www.youtube.com/watch?v=" + VIDEO_ID, "New", "https://i.ytimg.com/vi/x/hqdefault.jpg", "rss")));
        Assert.That(FeedsService.BuildVideoEmbed(new EmbedBuilder(), new MusicServiceUpload(VIDEO_ID, " ", "javascript:x"), "rss")
                .ImageUrl,
            Is.Null);

        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-27T00:00:00Z"));
        var health = new FeedHealth(time);
        const string key = "feed";
        health.BeginTick();
        health.Touch(key, FeedsService.YtFeedUrl(_live), sourceHost: MusicServiceClient.HTTP_CLIENT_NAME);
        health.EndTick();

        // Four days of service errors: the feed waits between tries, but no failure streak starts.
        for (var i = 0; i < 4 * 24; i++)
        {
            health.OnUnavailable(key);
            Assert.That(health.IsDue(key), Is.False);
            time.Advance(TimeSpan.FromHours(1));
            Assert.That(health.IsDue(key), Is.True);
        }

        var failure = health.OnFailure(key, (int)HttpStatusCode.NotFound, null);
        Assert.That((failure.StreakStarted, failure.Remove), Is.EqualTo((true, false)));
    }

    [Test]
    public async Task Streams_StartWithNamesWhichDifferOnlyInLetterCase()
    {
        // Both servers are on shard 1 of 2, the shard of the test client.
        const ulong first = 1UL << 22;
        const ulong second = 3UL << 22;
        await using (var ctx = _db.GetDbContext())
        {
            foreach (var (guildId, name) in new[] { (first, "Nadeko"), (second, "nadeko") })
            {
                await StreamNotificationService.TryAddFollowAsync(ctx,
                    new FollowedStream { GuildId = guildId, ChannelId = CHANNEL, Type = FollowedStream.FType.Trovo, Username = name },
                    -1);
            }
        }

        var creds = Substitute.For<IBotCreds>();
        creds.TotalShards.Returns(2);
        var streams = CreateStreams(creds);

        Assert.DoesNotThrowAsync(() => streams.OnReadyAsync());
        Assert.That((await streams.GetStreamsAsync(first)).Count + (await streams.GetStreamsAsync(second)).Count, Is.EqualTo(2));
    }

    [Test]
    public async Task Streams_EditRefusesADuplicateChannelAndTheSettingsAreSetNotToggled()    {
        var streams = CreateStreams();
        FollowedStream Row(ulong channelId, string? message)
            => new()
            {
                GuildId = GUILD,
                ChannelId = channelId,
                Type = FollowedStream.FType.Twitch,
                Username = "nadeko",
                PrettyName = "Nadeko",
                Message = message
            };

        var first = Row(CHANNEL, "live now");
        await using (var ctx = _db.GetDbContext())
        {
            await StreamNotificationService.TryAddFollowAsync(ctx, first, -1);
            await StreamNotificationService.TryAddFollowAsync(ctx, Row(OTHER_CHANNEL, null), -1);
        }

        var duplicate = await StreamEndpoints.UpdateStreamAsync(GUILD, first.Id, OTHER_CHANNEL, true, "changed", streams);
        Assert.That((Read(duplicate).Status, ErrorOf(duplicate)), Is.EqualTo((409, "duplicate_entry")));
        var unchanged = (await streams.GetStreamsAsync(GUILD)).Single(x => x.Id == first.Id);
        Assert.That((unchanged.ChannelId, unchanged.Message), Is.EqualTo((CHANNEL, "live now")));

        var moved = (FollowedStreamDto)Read(await StreamEndpoints.UpdateStreamAsync(GUILD, first.Id, THIRD_CHANNEL, false, null, streams)).Value!;
        Assert.That(moved,
            Is.EqualTo(new FollowedStreamDto(first.Id, "twitch", "Nadeko", "https://twitch.tv/nadeko", THIRD_CHANNEL.ToString(), "live now")));

        var cleared = (FollowedStreamDto)Read(await StreamEndpoints.UpdateStreamAsync(GUILD, first.Id, null, true, null, streams)).Value!;
        Assert.That((cleared.ChannelId, cleared.Message), Is.EqualTo((THIRD_CHANNEL.ToString(), (string?)null)));

        var otherGuild = await StreamEndpoints.UpdateStreamAsync(OTHER_GUILD, first.Id, CHANNEL, false, null, streams);
        Assert.That((Read(otherGuild).Status, ErrorOf(otherGuild)), Is.EqualTo((404, "stream_not_found")));

        await streams.SetStreamSettingsAsync(GUILD, true, true);
        for (var i = 0; i < 2; i++)
        {
            var set = (StreamSettingsDto)Read(await StreamEndpoints.UpdateSettingsAsync(GUILD, new UpdateStreamSettingsRequest(false), streams)).Value!;
            Assert.That(set, Is.EqualTo(new StreamSettingsDto(false, true)));
        }

        Assert.That(await streams.GetStreamSettingsAsync(GUILD), Is.EqualTo((false, true)));

        Assert.That((await streams.UnfollowStreamByIdAsync(GUILD, first.Id))?.Id, Is.EqualTo(first.Id));
        Assert.That(await streams.UnfollowStreamByIdAsync(GUILD, first.Id), Is.Null);
        Assert.That((await streams.GetStreamsAsync(GUILD)).Select(static x => x.ChannelId), Is.EqualTo(new[] { OTHER_CHANNEL }));
    }

    [Test]
    public void StreamLinks_ShowThePageOfEachPlatform()
    {
        var expected = new Dictionary<FollowedStream.FType, (string, string)>
        {
            [FollowedStream.FType.Twitch] = ("twitch", "https://twitch.tv/x"),
            [FollowedStream.FType.Picarto] = ("picarto", "https://picarto.tv/x"),
            [FollowedStream.FType.Youtube] = ("youtube", "https://www.youtube.com/channel/x"),
            [FollowedStream.FType.Facebook] = ("facebook", "https://facebook.com/x"),
            [FollowedStream.FType.Trovo] = ("trovo", "https://trovo.live/x"),
            [FollowedStream.FType.Kick] = ("kick", "https://kick.com/x")
        };

        foreach (var type in Enum.GetValues<FollowedStream.FType>())
            Assert.That((StreamEndpoints.PlatformName(type), StreamEndpoints.StreamUrl(type, "x")), Is.EqualTo(expected[type]), type.ToString());
    }

    private string LiveAnswer(string? body)
    {
        var entries = new List<string>();
        foreach (var id in JsonDocument.Parse(body!).RootElement.GetProperty("channels").EnumerateArray().Select(static x => x.GetString()!))
        {
            entries.Add(id == _live
                ? $$$"""{"id":"{{{id}}}","status":"live","name":"Live","avatar":"https://yt3.ggpht.com/a.jpg","live":{"videoId":"{{{VIDEO_ID}}}","title":"Now","thumbnail":"https://i.ytimg.com/vi/{{{VIDEO_ID}}}/hq.jpg","viewers":12,"startedAt":"2026-10-27T10:00:00Z"}}"""
                : id == _missing
                    ? $$"""{"id":"{{id}}","status":"missing","name":null,"avatar":null,"live":null}"""
                    : id == _unknown
                        ? $$"""{"id":"{{id}}","status":"unknown","name":null,"avatar":null,"live":null}"""
                        : $$"""{"id":"{{id}}","status":"offline","name":"Off","avatar":"javascript:x","live":null}""");
        }

        return $$"""{"channels":[{{string.Join(',', entries)}}]}""";
    }

    [Test]
    public async Task YouTubeLive_UsesTheServiceAndOnlyMissingChannelsAreDeletedAfter12Hours()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var provider = new YouTubeProvider(_factory, _config, _music);
        var checker = new NotifChecker([provider], time);
        var down = false;
        _handler.Respond = (_, body) => down
            ? Json(HttpStatusCode.ServiceUnavailable, """{"error":"no_exit"}""")
            : Json(HttpStatusCode.OK, LiveAnswer(body));

        var data = await provider.GetStreamDataAsync([_live, _offline, _unknown, _missing, "not-a-channel"]);
        Assert.That(data.Select(static d => (d.UniqueName, d.IsLive)), Is.EquivalentTo(new[] { (_live, true), (_offline, false) }));
        var live = data.Single(static d => d.IsLive);
        Assert.That((live.Name, live.Title, live.Viewers, live.StreamUrl, live.Preview),
            Is.EqualTo(("Live", "Now", 12, "https://youtube.com/watch?v=" + VIDEO_ID, $"https://i.ytimg.com/vi/{VIDEO_ID}/hq.jpg")));
        var offline = data.Single(static d => !d.IsLive);
        Assert.That((offline.Name, offline.AvatarUrl, offline.StreamUrl), Is.EqualTo(("Off", (string?)null, "https://www.youtube.com/channel/" + _offline)));
        Assert.That(provider.FailingStreams.Keys, Is.EqualTo(new[] { _missing }));

        // An outage keeps every state and adds no failing channel.
        down = true;
        Assert.That(await provider.GetStreamDataAsync([_live, _offline, _unknown]), Is.Empty);
        Assert.That(provider.FailingStreams.Keys, Is.EqualTo(new[] { _missing }));

        time.Advance(TimeSpan.FromHours(12) - TimeSpan.FromMinutes(1));
        Assert.That(checker.GetFailingStreams(TimeSpan.FromHours(12)), Is.Empty);
        time.Advance(TimeSpan.FromMinutes(2));
        Assert.That(checker.GetFailingStreams(TimeSpan.FromHours(12), remove: true),
            Is.EqualTo(new[] { new StreamDataKey(FollowedStream.FType.Youtube, _missing) }));
        Assert.That(provider.FailingStreams, Is.Empty);

        // A channel which answers again loses its failing mark.
        down = false;
        await provider.GetStreamDataAsync([_missing]);
        _handler.Respond = (_, _) => Json(HttpStatusCode.OK, $$"""{"channels":[{"id":"{{_missing}}","status":"offline","name":"Back","avatar":null,"live":null}]}""");
        await provider.GetStreamDataAsync([_missing]);
        Assert.That(provider.FailingStreams, Is.Empty);

        var many = Enumerable.Range(0, MusicServiceClient.MAX_LIVE_CHANNELS + 1).Select(static i => "UC" + i.ToString("D22")).ToList();
        _handler.Requests.Clear();
        _handler.Respond = (_, body) => Json(HttpStatusCode.OK, LiveAnswer(body));
        Assert.That(await provider.GetStreamDataAsync(many), Has.Count.EqualTo(many.Count));
        Assert.That(_handler.Requests.Select(static r => (r.Method, r.PathAndQuery)), Is.All.EqualTo((HttpMethod.Post, "/v1/live")));
        Assert.That(_handler.Requests, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task YouTubeLookup_TellsAMissingChannelFromAServiceWhichCannotAnswer()
    {
        var channel = ChannelId('c');
        var resolve = HttpStatusCode.OK;
        var liveStatus = "unknown";
        _handler.Respond = (req, _) => req.RequestUri!.AbsolutePath == "/v1/live"
            ? Json(HttpStatusCode.OK, $$"""{"channels":[{"id":"{{channel}}","status":"{{liveStatus}}","name":null,"avatar":null,"live":null}]}""")
            : resolve == HttpStatusCode.OK
                ? Json(HttpStatusCode.OK, $$"""{"id":"{{channel}}","name":"Nadeko","avatar":"https://yt3.ggpht.com/a.jpg"}""")
                : Json(resolve, resolve == HttpStatusCode.NotFound ? """{"error":"unavailable"}""" : """{"error":"x"}""");
        var checker = new NotifChecker([new YouTubeProvider(_factory, _config, _music)]);
        const string link = "https://www.youtube.com/@nadeko";

        // The channel exists but the service could not check it now: the bot follows it with the name of the resolve.
        var unknown = await checker.LookupByUrlAsync(link);
        Assert.That((unknown.Status, unknown.Platform, unknown.Data?.UniqueName, unknown.Data?.Name, unknown.Data?.IsLive),
            Is.EqualTo((StreamLookupStatus.Found, (FollowedStream.FType?)FollowedStream.FType.Youtube, channel, "Nadeko", (bool?)false)));

        // An offline channel which never streamed has no name in the live answer, and links with a tab or on m.youtube.com work.
        liveStatus = "offline";
        var offline = await checker.LookupByUrlAsync("https://m.youtube.com/c/nadeko/live");
        Assert.That((offline.Status, offline.Data?.Name, offline.Data?.AvatarUrl),
            Is.EqualTo((StreamLookupStatus.Found, "Nadeko", "https://yt3.ggpht.com/a.jpg")));

        liveStatus = "missing";
        Assert.That((await checker.LookupByUrlAsync(link)).Status, Is.EqualTo(StreamLookupStatus.NotFound));

        resolve = HttpStatusCode.NotFound;
        Assert.That((await checker.LookupByUrlAsync(link)).Status, Is.EqualTo(StreamLookupStatus.NotFound));
        resolve = HttpStatusCode.BadGateway;
        Assert.That((await checker.LookupByUrlAsync(link)).Status, Is.EqualTo(StreamLookupStatus.Unavailable));
        _handler.Respond = (_, _) => Json(HttpStatusCode.NotFound, """{"error":"not_found"}""");
        Assert.That((await _music.ResolveChannelAsync("@x")).Status, Is.EqualTo(MusicServiceStatus.Failed));

        Assert.That((await checker.LookupByUrlAsync("https://kick.com/nadeko")).Status, Is.EqualTo(StreamLookupStatus.Unsupported));

        var noService = new MusicServiceClient(_factory, _config, new BotPolicy(false)) { Key = string.Empty };
        var unconfigured = new NotifChecker([new YouTubeProvider(_factory, _config, noService)]);
        Assert.That(await unconfigured.LookupByUrlAsync(link),
            Is.EqualTo(new StreamLookup(StreamLookupStatus.Unavailable, null, FollowedStream.FType.Youtube)));
    }

    [Test]
    public void StreamKeys_MatchInAnyLetterCase()
    {
        var stored = new StreamDataKey(FollowedStream.FType.Youtube, _live);
        var fromData = new StreamData { StreamType = FollowedStream.FType.Youtube, UniqueName = _live.ToLowerInvariant() }.CreateKey();
        Assert.That(fromData, Is.EqualTo(stored));
        Assert.That(new HashSet<StreamDataKey> { stored }.Contains(fromData), Is.True);
        Assert.That(new StreamDataKey(FollowedStream.FType.Twitch, _live), Is.Not.EqualTo(stored));

        var row = new FollowedStream { ChannelId = CHANNEL, Username = "Nadeko ", Type = FollowedStream.FType.Twitch };
        var same = new FollowedStream { ChannelId = CHANNEL, Username = "nadeko", Type = FollowedStream.FType.Twitch };
        Assert.That(new HashSet<FollowedStream> { row }.Contains(same), Is.True);

        // A stream goes offline after two offline checks in a row, also when the platform changes the name's case.
        static StreamData Trovo(string name, bool live)
            => new() { StreamType = FollowedStream.FType.Trovo, UniqueName = name, Name = name, IsLive = live };

        // An answer without a name or avatar (YouTube offline) keeps the last ones for the alert.
        static StreamData Nameless(string name)
            => new() { StreamType = FollowedStream.FType.Trovo, UniqueName = name, Name = string.Empty, IsLive = false };

        var checker = new NotifChecker([]);
        var first = Trovo("Nadeko", true);
        first.AvatarUrl = "https://example.com/a.png";
        checker.AddLastData(new StreamDataKey(FollowedStream.FType.Trovo, "Nadeko"), first, true);
        var online = new List<StreamData>();
        var offline = new List<StreamData>();
        checker.ApplyCheck([Nameless("nadeko")], online, offline);
        Assert.That((online.Count, offline.Count), Is.EqualTo((0, 0)));
        checker.ApplyCheck([Nameless("NADEKO")], online, offline);
        Assert.That((online.Count, offline.Count, offline[0].Name, offline[0].AvatarUrl),
            Is.EqualTo((0, 1, "Nadeko", "https://example.com/a.png")));
        checker.ApplyCheck([Trovo("Nadeko", true)], online, offline);
        Assert.That((online.Count, offline.Count), Is.EqualTo((1, 1)));
        Assert.That(checker.GetAllData(), Has.Count.EqualTo(1));

        // With no name ever known, the alert shows the channel id.
        checker.ApplyCheck([Nameless("other")], online, offline);
        Assert.That(checker.GetAllData()[new StreamDataKey(FollowedStream.FType.Trovo, "other")]!.Name, Is.EqualTo("other"));
    }

    [Test]
    public void UpdateMessage_AMissingFieldKeepsItNullClearsItAndTextSetsIt()
    {
        (bool Ok, bool Set, string? Message) Parse(string json)
        {
            var body = JsonSerializer.Deserialize(json, ApiJsonContext.Default.UpdateFeedRequest)!;
            var ok = NotificationInput.TryReadMessage(body.Message, out var set, out var message);
            return (ok, set, message);
        }

        Assert.That(Parse("""{"channelId":"1"}"""), Is.EqualTo((true, false, (string?)null)));
        Assert.That(Parse("""{"message":null}"""), Is.EqualTo((true, true, (string?)null)));
        Assert.That(Parse("""{"message":""}"""), Is.EqualTo((true, true, (string?)null)));
        Assert.That(Parse("""{"message":"<@&1> live"}"""), Is.EqualTo((true, true, (string?)"<@&1> live")));
        Assert.That(Parse($$"""{"message":"{{new string('a', NotificationInput.MAX_MESSAGE_LENGTH + 1)}}"}""").Ok, Is.False);
        Assert.That(Parse("""{"message":5}""").Ok, Is.False);
        Assert.That(NotificationInput.IsMessage(new string('a', NotificationInput.MAX_MESSAGE_LENGTH)), Is.True);
    }
}
