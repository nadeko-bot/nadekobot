#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Time.Testing;
using Nadeko.Common;
using NadekoBot.Common;
using NadekoBot.Common.Configs;
using NadekoBot.Modules.Music;
using NadekoBot.Modules.Music.Resolvers;
using NadekoBot.Modules.Music.Services;
using NadekoBot.Modules.Searches;
using NadekoBot.Services;
using NadekoBot.Voice;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests;

public sealed class MusicServiceTests
{
    private const string BASE_URL = "http://10.0.0.5:8080";
    private const string KEY = "nadeko-public.test-signature";
    private const string VIDEO_ID = "dQw4w9WgXcQ";
    private const string OTHER_ID = "9bZkp7q5f_0";
    private const string THIRD_ID = "kJQP7kiw5Fk";
    private const string PLAYLIST_ID = "PLabcdefghijklmnop";

    private sealed class FakeServiceHandler : HttpMessageHandler
    {
        public readonly List<(HttpMethod Method, string PathAndQuery, string? Auth, string? Body)> Requests = [];
        public Func<HttpRequestMessage, HttpResponseMessage>? Respond;
        public bool Throw;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            lock (Requests)
                Requests.Add((request.Method, request.RequestUri!.PathAndQuery, request.Headers.Authorization?.ToString(), body));

            if (Throw)
                throw new HttpRequestException("connection refused");

            return Respond!(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string TrackJson(string id, string title)
        => $$"""{"id":"{{id}}","title":"{{title}}","durationSec":212,"thumbnail":"https://i.ytimg.com/vi/{{id}}/hq.jpg","url":"https://www.youtube.com/watch?v={{id}}"}""";

    private FakeServiceHandler _handler = null!;
    private SearchesConfigService _config = null!;
    private MusicServiceClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _handler = new FakeServiceHandler();
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(_handler, false));

        _config = new SearchesConfigService(new YamlSeria(), Substitute.For<IPubSub>(), new BotPolicy(false));
        _config.ModifyConfig(c => c.MusicServiceUrl = BASE_URL + "/");

        _client = new MusicServiceClient(factory, _config, new BotPolicy(false)) { Key = KEY };
    }

    [TearDown]
    public void TearDown()
    {
        _config.ModifyConfig(c => c.MusicServiceUrl = string.Empty);
        _handler.Dispose();
    }

    [Test]
    public void TrustedStream_OnlyYoutubeOnTheServiceOrigin()
    {
        var audio = _client.GetAudioUrl(VIDEO_ID)!;
        Assert.That(audio, Is.EqualTo($"{BASE_URL}/v1/audio/{VIDEO_ID}"));
        Assert.That(MusicPlayer.IsTrustedServiceStream(MusicPlatform.Youtube, _client.BaseUrl, audio), Is.True);

        foreach (var other in new[]
                 {
                     "http://10.0.0.5:8081/v1/audio/" + VIDEO_ID, "http://10.0.0.6:8080/v1/audio/" + VIDEO_ID,
                     "https://10.0.0.5:8080/v1/audio/" + VIDEO_ID, "file:///etc/passwd", "not a url"
                 })
            Assert.That(MusicPlayer.IsTrustedServiceStream(MusicPlatform.Youtube, _client.BaseUrl, other), Is.False, other);

        Assert.That(MusicPlayer.IsTrustedServiceStream(MusicPlatform.Radio, _client.BaseUrl, audio), Is.False);
        Assert.That(MusicPlayer.IsTrustedServiceStream(MusicPlatform.Youtube, null, audio), Is.False);
        Assert.That(_client.GetAudioUrl("../../etc"), Is.Null);
        Assert.That(MusicServiceClient.ParseBaseUrl("ftp://10.0.0.5"), Is.Null);
        Assert.That(MusicServiceClient.ParseBaseUrl("http://u:p@10.0.0.5"), Is.Null);
    }

    [Test]
    public void FfmpegArguments_SendTheKeyWithoutAProxyAndRetry403()
    {
        var headers = _client.FfmpegHeaders!;
        Assert.That(headers, Is.EqualTo($"Authorization: Bearer {KEY}\r\n"));

        var args = FfmpegTrackDataSource.BuildArguments(_client.GetAudioUrl(VIDEO_ID)!, false, null, headers);
        Assert.That(args, Does.Not.Contain("-http_proxy"));
        Assert.That(args[Array.IndexOf(args, "-headers") + 1], Is.EqualTo(headers));
        Assert.That(args[Array.IndexOf(args, "-reconnect_on_http_error") + 1], Is.EqualTo("403"));
        Assert.That(args[Array.IndexOf(args, "-protocol_whitelist") + 1], Is.EqualTo("http,https,tcp,tls,crypto"));
        Assert.That(Array.IndexOf(args, "-headers"), Is.LessThan(Array.IndexOf(args, "-i")));

        var proxied = FfmpegTrackDataSource.BuildArguments("https://example.com/a.mp3", false, "http://127.0.0.1:1", null);
        Assert.That(proxied, Does.Contain("-http_proxy"));
        Assert.That(proxied, Does.Not.Contain("-headers"));

        var local = FfmpegTrackDataSource.BuildArguments("/music/a.mp3", true, null, headers);
        Assert.That(local, Does.Not.Contain("-headers"));
        Assert.That(local, Does.Not.Contain("-reconnect_on_http_error"));
        Assert.That(local[Array.IndexOf(local, "-protocol_whitelist") + 1], Is.EqualTo("file"));
    }

    [Test]
    public async Task Resolver_ReadsTracksPlaylistsAndSearchWithTheKey()
    {
        _handler.Respond = req => req.RequestUri!.AbsolutePath switch
        {
            "/v1/search" => Json(HttpStatusCode.OK,
                $$"""{"tracks":[{{TrackJson(VIDEO_ID, "First")}},{"id":"bad/id/here","title":"x"},{{TrackJson(OTHER_ID, "Second")}}]}"""),
            "/v1/tracks/" + VIDEO_ID => Json(HttpStatusCode.OK, TrackJson(VIDEO_ID, "Linked")),
            "/v1/search/playlists" => Json(HttpStatusCode.OK,
                $$"""{"playlists":[{"id":"{{PLAYLIST_ID}}","title":"Mix","url":"u"}]}"""),
            "/v1/playlists/" + PLAYLIST_ID => Json(HttpStatusCode.OK,
                $$"""{"id":"{{PLAYLIST_ID}}","title":"Mix","tracks":[{{TrackJson(VIDEO_ID, "A")}},{{TrackJson(OTHER_ID, "B")}}]}"""),
            _ => Json(HttpStatusCode.NotFound, """{"error":"not_found"}""")
        };

        var resolver = new ServiceYoutubeResolver(_client);

        var searched = await resolver.ResolveByQueryAsync("never gonna give you up");
        Assert.That((searched?.Id, searched?.Title, searched?.Duration), Is.EqualTo(((string?)VIDEO_ID, (string?)"First", (TimeSpan?)TimeSpan.FromSeconds(212))));
        Assert.That(_handler.Requests[^1].PathAndQuery, Is.EqualTo("/v1/search?q=never%20gonna%20give%20you%20up&limit=1"));

        var linked = await resolver.ResolveByQueryAsync($"https://www.youtube.com/watch?v={VIDEO_ID}&t=10");
        Assert.That(linked?.Title, Is.EqualTo("Linked"));
        Assert.That(linked?.Platform, Is.EqualTo(MusicPlatform.Youtube));

        var fromLink = new List<string>();
        await foreach (var t in resolver.ResolveTracksFromPlaylistAsync($"https://www.youtube.com/playlist?list={PLAYLIST_ID}"))
            fromLink.Add(t.Title);
        Assert.That(fromLink, Is.EqualTo(new[] { "A", "B" }));

        var fromWords = new List<string>();
        await foreach (var t in resolver.ResolveTracksFromPlaylistAsync("chill mix"))
            fromWords.Add(t.Id);
        Assert.That(fromWords, Is.EqualTo(new[] { VIDEO_ID, OTHER_ID }));

        var videos = await new NadekoBot.Modules.Searches.Youtube.ServiceYtSearchService(_client).SearchAsync("psy");
        Assert.That(videos!.Length, Is.EqualTo(2));
        Assert.That(_handler.Requests[^1].PathAndQuery, Does.EndWith("&limit=5"));

        Assert.That(_handler.Requests, Has.All.Matches<(HttpMethod, string, string? Auth, string?)>(r => r.Auth == "Bearer " + KEY));
        Assert.That(await resolver.GetStreamUrl(VIDEO_ID), Is.EqualTo($"{BASE_URL}/v1/audio/{VIDEO_ID}"));
    }

    [Test]
    public async Task Resolver_ErrorAnswersGiveNoTrack()
    {
        var resolver = new ServiceYoutubeResolver(_client);
        foreach (var (status, json) in new[]
                 {
                     (HttpStatusCode.NotFound, """{"error":"unavailable"}"""),
                     (HttpStatusCode.UnprocessableEntity, """{"error":"live"}"""),
                     (HttpStatusCode.ServiceUnavailable, """{"error":"no_exit"}"""),
                     (HttpStatusCode.BadGateway, """{"error":"youtube_failed"}"""),
                     (HttpStatusCode.Unauthorized, """{"error":"unauthorized"}"""),
                     (HttpStatusCode.OK, "not json"),
                     (HttpStatusCode.OK, """{"id":"short","title":"x"}""")
                 })
        {
            _handler.Respond = _ => Json(status, json);
            Assert.That(await resolver.ResolveByIdAsync(VIDEO_ID), Is.Null, json);
        }

        var calls = _handler.Requests.Count;
        Assert.That(await resolver.ResolveByIdAsync("../v1/x"), Is.Null);
        Assert.That(_handler.Requests.Count, Is.EqualTo(calls), "an invalid id never reaches the service");

        _handler.Throw = true;
        Assert.That(await resolver.ResolveByQueryAsync("anything"), Is.Null);
    }

    [Test]
    public async Task Autoplay_ReadsRelatedSongsAndPicksOneWhichIsNotQueued()
    {
        _handler.Respond = req => req.RequestUri!.AbsolutePath switch
        {
            "/v1/related/" + VIDEO_ID => Json(HttpStatusCode.OK,
                $$"""{"tracks":[{{TrackJson(VIDEO_ID, "Seed")}},{"id":"bad/id","title":"x"},{{TrackJson(OTHER_ID, "Queued")}},{{TrackJson(THIRD_ID, "Fresh")}}]}"""),
            "/v1/related/" + OTHER_ID => Json(HttpStatusCode.OK, """{"tracks":[]}"""),
            _ => Json(HttpStatusCode.ServiceUnavailable, """{"error":"no_exit"}""")
        };

        var tracks = await _client.GetRelatedAsync(VIDEO_ID, 100);
        Assert.That(_handler.Requests[^1].PathAndQuery, Is.EqualTo($"/v1/related/{VIDEO_ID}?limit={MusicServiceClient.MAX_RELATED_LIMIT}"));
        Assert.That(tracks!.Select(static t => t.Id), Is.EqualTo(new[] { VIDEO_ID, OTHER_ID, THIRD_ID }));
        Assert.That(await _client.GetRelatedAsync(OTHER_ID, 5), Is.Empty);
        Assert.That(await _client.GetRelatedAsync(THIRD_ID, 5), Is.Null, "an error is not an empty list");

        var calls = _handler.Requests.Count;
        Assert.That(await _client.GetRelatedAsync("../v1/x", 5), Is.Null);
        Assert.That(_handler.Requests.Count, Is.EqualTo(calls));

        var queued = Substitute.For<IQueuedTrackInfo>();
        queued.Platform.Returns(MusicPlatform.Youtube);
        queued.TrackInfo.Returns(new RemoteTrackInfo(OTHER_ID, "Queued", "u", "", TimeSpan.Zero, MusicPlatform.Youtube));
        for (var seed = 0; seed < 20; seed++)
            Assert.That(MusicPlayer.PickRelated(tracks, VIDEO_ID, [queued], new Random(seed))?.Id, Is.EqualTo(THIRD_ID));

        Assert.That(MusicPlayer.PickRelated(tracks[..2], VIDEO_ID, [queued], new Random(1)), Is.Null);

        var radio = Substitute.For<IQueuedTrackInfo>();
        radio.Platform.Returns(MusicPlatform.Radio);
        radio.TrackInfo.Returns(new RemoteTrackInfo(OTHER_ID, "Radio", "u", "", TimeSpan.Zero, MusicPlatform.Radio));
        Assert.That(MusicPlayer.PickRelated(tracks[..3], VIDEO_ID, [radio], new Random(1))?.Id, Is.EqualTo(OTHER_ID),
            "only a queued YouTube song blocks the pick");
    }

    [Test]
    public async Task Prefetch_PostsTheIdAndNeverThrows()
    {
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.Accepted);
        await _client.PrefetchAsync(VIDEO_ID);
        var (method, path, auth, body) = _handler.Requests[^1];
        Assert.That((method, path, auth), Is.EqualTo((HttpMethod.Post, "/v1/prefetch", (string?)("Bearer " + KEY))));
        Assert.That(body, Is.EqualTo($$"""{"ids":["{{VIDEO_ID}}"]}"""));

        _handler.Respond = _ => Json(HttpStatusCode.TooManyRequests, """{"error":"busy"}""");
        await _client.PrefetchAsync(VIDEO_ID);

        _handler.Throw = true;
        Assert.DoesNotThrowAsync(() => _client.PrefetchAsync(VIDEO_ID));

        var calls = _handler.Requests.Count;
        await _client.PrefetchAsync("not-an-id");
        Assert.That(_handler.Requests.Count, Is.EqualTo(calls));
    }

    [Test]
    public async Task PlaylistLoad_ReleasesTheLoadLockAfterAFinishedOrFailedLoad()
    {
        using var discord = new DiscordSocketClient();
        var service = new MusicService(null!, null!, null!, null!, null!, discord, null!, null!, null!, null!, null!, null!, null!);
        (string, MusicPlatform)[] songs = [("song", MusicPlatform.Youtube)];

        var failing = Substitute.For<IMusicPlayer>();
        failing.EnqueueManyAsync(default!, default!, default)
            .ReturnsForAnyArgs(Task.FromException(new InvalidOperationException("the player was killed")));
        var finishing = Substitute.For<IMusicPlayer>();
        finishing.EnqueueManyAsync(default!, default!, default).ReturnsForAnyArgs(Task.CompletedTask);

        foreach (var mp in new[] { failing, finishing })
        {
            Assert.That(service.TryEnterPlaylistLoad(), Is.True);
            Assert.That(service.TryEnterPlaylistLoad(), Is.False, "a second load waits for the first");
            service.StartPlaylistLoad(mp, songs, "queuer");

            var released = false;
            for (var i = 0; i < 200 && !released; i++)
            {
                await Task.Delay(10);
                released = service.TryEnterPlaylistLoad();
            }

            Assert.That(released, Is.True);
            service.ExitPlaylistLoad();
        }
    }

    [Test]
    public async Task YoutubePlaylist_KeepsTheTracksQueuedBeforeAFailure()
    {
        var resolver = Substitute.For<IYoutubeResolver>();
        var factory = Substitute.For<IYoutubeResolverFactory>();
        factory.GetYoutubeResolver().Returns(resolver);
        using var discord = new DiscordSocketClient();
        var service = new MusicService(null!, null!, null!, factory, null!, discord, null!, null!, null!, null!, null!, null!, null!);
        var mp = Substitute.For<IMusicPlayer>();
        mp.EnqueueResolvedTrack(Arg.Any<ITrackInfo>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
          .Returns((Substitute.For<IQueuedTrackInfo>(), 0));

        resolver.ResolveTracksFromPlaylistAsync(Arg.Any<string>()).Returns(_ => FailingPlaylistInternal(2));
        var result = await service.EnqueueYoutubePlaylistAsync(mp, "mix", "queuer");
        Assert.That((result.Status, result.Added), Is.EqualTo((MusicAddStatus.Added, 2)));
        mp.Received(2).EnqueueResolvedTrack(Arg.Any<ITrackInfo>(), "queuer", false, false);

        resolver.ResolveTracksFromPlaylistAsync(Arg.Any<string>()).Returns(_ => FailingPlaylistInternal(0));
        Assert.ThrowsAsync<HttpRequestException>(() => service.EnqueueYoutubePlaylistAsync(mp, "mix", "queuer"));
    }

    private static async IAsyncEnumerable<ITrackInfo> FailingPlaylistInternal(int tracksBeforeFailure)
    {
        for (var i = 0; i < tracksBeforeFailure; i++)
        {
            await Task.Yield();
            yield return new RemoteTrackInfo(VIDEO_ID, "Song " + i, "u", "", TimeSpan.Zero, MusicPlatform.Youtube);
        }

        throw new HttpRequestException("the playlist stopped");
    }

    [Test]
    public void PlayerMessages_NeedViewSendAndEmbed()
    {
        var now = DateTimeOffset.UtcNow;
        var all = new ChannelPermissions(viewChannel: true, sendMessages: true, embedLinks: true);
        var allThread = new ChannelPermissions(viewChannel: true, sendMessagesInThreads: true, embedLinks: true);
        Assert.That(MusicService.CanSendEmbed(all, false, false, null, now), Is.True);
        Assert.That(MusicService.CanSendEmbed(allThread, true, false, null, now), Is.True);
        Assert.That(MusicService.CanSendEmbed(all, false, false, now.AddMinutes(-1), now), Is.True, "an ended timeout");

        foreach (var (perms, thread, outside, until) in new (ChannelPermissions, bool, bool, DateTimeOffset?)[]
                 {
                     (new ChannelPermissions(sendMessages: true, embedLinks: true), false, false, null),
                     (new ChannelPermissions(viewChannel: true, embedLinks: true), false, false, null),
                     (new ChannelPermissions(viewChannel: true, sendMessages: true), false, false, null),
                     (all, true, false, null),
                     (allThread, true, true, null),
                     (all, false, false, now.AddMinutes(5))
                 })
            Assert.That(MusicService.CanSendEmbed(perms, thread, outside, until, now), Is.False,
                $"{perms.RawValue} thread={thread} outside={outside} until={until}");
    }

    [Test]
    public void Queue_NextTrackFollowsRepeat()
    {
        var queue = new MusicQueue();
        Assert.That(queue.GetNext(true), Is.Null);

        queue.Enqueue(new RemoteTrackInfo(VIDEO_ID, "A", "u", "", TimeSpan.Zero, MusicPlatform.Youtube), "q", out _);
        Assert.That(queue.GetNext(true), Is.Null, "the only track is already playing");

        queue.Enqueue(new RemoteTrackInfo(OTHER_ID, "B", "u", "", TimeSpan.Zero, MusicPlatform.Youtube), "q", out _);
        Assert.That(queue.GetNext(false)?.Title, Is.EqualTo("B"));

        queue.Advance();
        Assert.That(queue.GetNext(false), Is.Null);
        Assert.That(queue.GetNext(true)?.Title, Is.EqualTo("A"));
    }

    [Test]
    public async Task FileCache_RetriesTheSameLinkAfter403()
    {
        const int forbiddenAnswers = 2;
        const int sizeBytes = 1024;
        var url = "https://rr1---sn-test.googlevideo.com/videoplayback?id=" + Guid.NewGuid().ToString("N");
        var trackId = Guid.NewGuid().ToString("N")[..11];

        var forbiddenLeft = forbiddenAnswers;
        using var handler = new FakeServiceHandler
        {
            Respond = _ => Interlocked.Decrement(ref forbiddenLeft) >= 0
                ? new HttpResponseMessage(HttpStatusCode.Forbidden)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[sizeBytes]) }
        };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, false));

        var time = new FakeTimeProvider();
        var cache = new AudioFileCacheService(null!, factory, new BotPolicy(false), null!, time);

        var resolves = 0;
        var (path, state) = cache.GetOrStartDownload(trackId, MusicPlatform.Youtube, () =>
        {
            Interlocked.Increment(ref resolves);
            return Task.FromResult<string?>(url);
        });

        try
        {
            for (var i = 0; i < 200 && !state!.IsDone; i++)
            {
                await Task.Delay(10);
                time.Advance(TimeSpan.FromSeconds(1));
            }

            Assert.That(state!.IsComplete, Is.True);
            Assert.That(cache.GetCachedPath(trackId, MusicPlatform.Youtube), Is.Not.Null);
            Assert.That(resolves, Is.EqualTo(1));
            Assert.That(handler.Requests, Has.Count.EqualTo(3));
            Assert.That(handler.Requests, Has.All.Matches<(HttpMethod, string Path, string?, string?)>(r => url.EndsWith(r.Path)));
        }
        finally
        {
            DeleteCachedInternal(cache, path, trackId);
        }
    }

    [Test]
    public async Task FileCache_KeepsNoCutSongWhenTheLinkDies()
    {
        const int sizeBytes = 1024;
        var url = "https://rr1---sn-test.googlevideo.com/videoplayback?id=" + Guid.NewGuid().ToString("N");
        var trackId = Guid.NewGuid().ToString("N")[..11];

        var first = 1;
        using var handler = new FakeServiceHandler
        {
            Respond = _ =>
            {
                if (Interlocked.Exchange(ref first, 0) == 0)
                    return new HttpResponseMessage(HttpStatusCode.Forbidden);

                var content = new StreamContent(new BrokenStream(sizeBytes / 2));
                content.Headers.ContentLength = sizeBytes;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            }
        };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, false));

        var time = new FakeTimeProvider();
        var cache = new AudioFileCacheService(null!, factory, new BotPolicy(false), null!, time);
        var (path, state) = cache.GetOrStartDownload(trackId, MusicPlatform.Youtube, () => Task.FromResult<string?>(url));

        try
        {
            for (var i = 0; i < 500 && !state!.IsDone; i++)
            {
                await Task.Delay(10);
                time.Advance(TimeSpan.FromSeconds(1));
            }

            Assert.That(state!.IsFailed, Is.True);
            Assert.That(cache.GetCachedPath(trackId, MusicPlatform.Youtube), Is.Null);
        }
        finally
        {
            DeleteCachedInternal(cache, path, trackId);
        }
    }

    private static void DeleteCachedInternal(AudioFileCacheService cache, string? path, string trackId)
    {
        var final = cache.GetCachedPath(trackId, MusicPlatform.Youtube);
        foreach (var p in new[] { path, final })
        {
            if (p is not null && File.Exists(p))
                File.Delete(p);
        }
    }

    private sealed class BrokenStream(int goodBytes) : Stream
    {
        private int _left = goodBytes;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_left == 0)
                throw new IOException("connection reset");

            var n = Math.Min(count, _left);
            _left -= n;
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
