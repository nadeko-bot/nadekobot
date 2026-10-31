#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Microsoft.AspNetCore.Http;
using NadekoBot.Tests.Waifu;
using NadekoBot.Db.Models;
using NadekoBot.Hosted;
using NadekoBot.Hosted.Endpoints;
using NadekoBot.Modules.Music;
using NadekoBot.Modules.Music.Services;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests;

[TestFixture]
public sealed class ApiMusicTests
{
    [Test]
    public void VideoLinks_AcceptOneYouTubeVideoInEachFormAndNothingElse()
    {
        string[] accepted =
        [
            "https://www.youtube.com/watch?v=dQw4w9WgXcQ", "http://youtube.com/watch?list=PLabcdefghijklmnop&v=dQw4w9WgXcQ",
            "https://m.youtube.com/watch?v=dQw4w9WgXcQ", "https://music.youtube.com/watch?v=dQw4w9WgXcQ&si=x",
            "https://youtu.be/dQw4w9WgXcQ", " https://YOUTU.BE/dQw4w9WgXcQ?t=42 ", "https://youtu.be/dQw4w9WgXcQ/",
            "https://www.youtube.com/watch/?v=dQw4w9WgXcQ"
        ];
        foreach (var link in accepted)
        {
            Assert.That(PlaylistEndpoints.TryNormalizeVideoLink(link, out var normalized), Is.True, link);
            Assert.That(normalized, Is.EqualTo("https://youtube.com/watch?v=dQw4w9WgXcQ"), link);
        }

        string?[] refused =
        [
            null, "dQw4w9WgXcQ", "https://soundcloud.com/a/b", "https://youtube.com/playlist?list=PLabcdefghijklmnop",
            "https://youtube.com/watch?v=dQw4w9WgXc", "https://youtube.com/watch?v=dQw4w9WgXcQQ", "https://youtube.com/watch?vv=dQw4w9WgXcQ",
            "https://youtube.com.evil.com/watch?v=dQw4w9WgXcQ", "https://evil.com/watch?v=dQw4w9WgXcQ",
            "https://user:pw@youtube.com/watch?v=dQw4w9WgXcQ", "https://youtube.com:8443/watch?v=dQw4w9WgXcQ",
            "ftp://youtube.com/watch?v=dQw4w9WgXcQ", "file:///watch?v=dQw4w9WgXcQ", "https://youtu.be/dQw4w9WgXcQ/x"
        ];
        Assert.That(refused.Where(static l => PlaylistEndpoints.TryNormalizeVideoLink(l, out _)), Is.Empty);
    }

    [Test]
    public void State_ListsTheQueueInOrderWithPlatformsLengthsAndTheCurrentTrack()
    {
        var queue = new MusicQueue();
        queue.Enqueue(new RemoteTrackInfo("dQw4w9WgXcQ", "Song", "https://youtube.com/watch?v=dQw4w9WgXcQ", "https://i.ytimg.com/a.jpg", TimeSpan.FromSeconds(212.6), MusicPlatform.Youtube), "Kwoth", out _);
        queue.Enqueue(new SimpleTrackInfo("Radio", "https://radio.example/a.m3u", "", TimeSpan.MaxValue, MusicPlatform.Radio), "Kwoth", out _);
        queue.Enqueue(new SimpleTrackInfo("File", "/music/a.mp3", "", TimeSpan.Zero, MusicPlatform.Local), "Nadeko", out _);
        var tracks = queue.List();

        var mp = Substitute.For<IMusicPlayer>();
        mp.GetQueuedTracks().Returns(tracks);
        mp.GetCurrentTrack(out Arg.Any<int>()).Returns(x =>
        {
            x[0] = 1;
            return tracks.ElementAt(1);
        });
        mp.Volume.Returns(0.35f);
        mp.Repeat.Returns(PlayerRepeatType.Track);
        mp.IsStopped.Returns(true);
        mp.MaxQueueSize.Returns(500);

        var state = MusicEndpoints.ToStateDto("42", mp);

        Assert.That((state.VoiceChannelId, state.Player, state.Stopped, state.Paused, state.Repeat, state.Volume, state.CurrentIndex, state.MaxQueueSize),
            Is.EqualTo(("42", true, true, false, "track", 35, (int?)1, 500)));
        Assert.That(state.Tracks.Select(static t => (t.Index, t.Id, t.Platform, t.DurationSec, t.Thumbnail, t.Queuer)),
            Is.EqualTo(new[]
            {
                (0, "dQw4w9WgXcQ", "youtube", 212, (string?)"https://i.ytimg.com/a.jpg", "Kwoth"),
                (1, "", "radio", 0, null, "Kwoth"),
                (2, "", "local", 0, null, "Nadeko")
            }));

        mp.GetCurrentTrack(out Arg.Any<int>()).Returns((IQueuedTrackInfo?)null);
        Assert.That(MusicEndpoints.ToStateDto(null, mp).CurrentIndex, Is.Null);
    }

    [Test]
    public void VoiceChangeLimiter_GivesEachServerFivePermitsThenTheWait()
    {
        using var limiter = new VoiceChangeLimiter();
        for (var i = 0; i < ApiLimits.VOICE_CHANGES_PER_GUILD; i++)
            Assert.That(limiter.TryAcquire(1), Is.Null);

        Assert.That(limiter.TryAcquire(1), Is.InRange(1, (int)ApiLimits.VoiceChangeWindow.TotalSeconds));
        Assert.That(limiter.TryAcquire(2), Is.Null);
    }

    [Test]
    public void QueueRoutes_ChangeThePlayerAndAnswerWithTheStateOrNoPlayer()
    {
        var mp = PlayerWithQueue();
        var music = MusicWithPlayer(mp);
        var first = mp.GetQueuedTracks().First();
        mp.TryRemoveTrackAt(1, out Arg.Any<IQueuedTrackInfo?>()).Returns(true);
        mp.MoveTrack(0, 2).Returns(first);
        mp.MoveTrack(5, 0).Returns((IQueuedTrackInfo?)null);
        mp.MoveTo(2).Returns(true);

        Assert.That(Read(MusicEndpoints.RemoveTrackAt(GUILD, VOICE, 1, music)).Status, Is.EqualTo(200));
        Assert.That(Read(MusicEndpoints.MoveTrackTo(GUILD, VOICE, new MoveTrackRequest(0, 2), music)).Value,
            Is.TypeOf<MusicStateDto>().With.Property(nameof(MusicStateDto.VoiceChannelId)).EqualTo(VOICE.ToString()));
        Assert.That(Read(MusicEndpoints.PlayTrackAt(GUILD, VOICE, new PlayTrackRequest(2), music)).Status, Is.EqualTo(200));
        Assert.That(Read(MusicEndpoints.ShuffleQueue(GUILD, VOICE, music)).Status, Is.EqualTo(200));
        mp.Received(1).ShuffleQueue();
        music.Received(1).AttachProxy(GUILD);

        IResult[] refused =
        [
            MusicEndpoints.RemoveTrackAt(GUILD, VOICE, 9, music),
            MusicEndpoints.MoveTrackTo(GUILD, VOICE, new MoveTrackRequest(1, 1), music),
            MusicEndpoints.MoveTrackTo(GUILD, VOICE, new MoveTrackRequest(-1, 0), music),
            MusicEndpoints.MoveTrackTo(GUILD, VOICE, new MoveTrackRequest(5, 0), music),
            MusicEndpoints.PlayTrackAt(GUILD, VOICE, new PlayTrackRequest(7), music)
        ];
        Assert.That(refused.Select(ErrorOf), Is.All.EqualTo("invalid_value"));

        IResult[] noPlayer =
        [
            MusicEndpoints.RemoveTrackAt(OTHER_GUILD, null, 0, music),
            MusicEndpoints.MoveTrackTo(OTHER_GUILD, null, new MoveTrackRequest(0, 1), music),
            MusicEndpoints.PlayTrackAt(OTHER_GUILD, null, new PlayTrackRequest(0), music),
            MusicEndpoints.ShuffleQueue(OTHER_GUILD, null, music)
        ];
        Assert.That(noPlayer.Select(static r => (Read(r).Status, ErrorOf(r))), Is.All.EqualTo((409, "no_player")));
    }

    [Test]
    public async Task PlayerSettings_ValidateFirstSaveRepeatAndVolumeAndResumeAStoppedPlayerWithAPermit()
    {
        var mp = PlayerWithQueue();
        var music = MusicWithPlayer(mp);
        using var limiter = new VoiceChangeLimiter();

        Assert.That(ErrorOf(await MusicEndpoints.UpdatePlayerAsync(GUILD, VOICE, new UpdateMusicRequest(Volume: 101), music, limiter)), Is.EqualTo("invalid_value"));
        Assert.That(ErrorOf(await MusicEndpoints.UpdatePlayerAsync(GUILD, VOICE, new UpdateMusicRequest(Repeat: "loop", Volume: 30), music, limiter)), Is.EqualTo("invalid_value"));
        Assert.That(ErrorOf(await MusicEndpoints.UpdatePlayerAsync(OTHER_GUILD, null, new UpdateMusicRequest(Paused: true), music, limiter)), Is.EqualTo("no_player"));
        await music.DidNotReceiveWithAnyArgs().SetVolumeAsync(default, default);

        var ok = await MusicEndpoints.UpdatePlayerAsync(GUILD, VOICE, new UpdateMusicRequest(true, "track", 30), music, limiter);
        Assert.That(Read(ok).Status, Is.EqualTo(200));
        await music.Received(1).SetRepeatAsync(GUILD, PlayerRepeatType.Track);
        await music.Received(1).SetVolumeAsync(GUILD, 30);
        mp.Received(1).SetPaused(true);

        // A playing player skips without a voice permit.
        for (var i = 0; i <= ApiLimits.VOICE_CHANGES_PER_GUILD; i++)
            Assert.That(Read(await MusicEndpoints.SkipAsync(GUILD, VOICE, music, limiter)).Status, Is.EqualTo(200));

        mp.IsStopped.Returns(true);
        for (var i = 0; i < ApiLimits.VOICE_CHANGES_PER_GUILD; i++)
            Assert.That(Read(await MusicEndpoints.UpdatePlayerAsync(GUILD, VOICE, new UpdateMusicRequest(Paused: false), music, limiter)).Status, Is.EqualTo(200));

        var limited = await MusicEndpoints.SkipAsync(GUILD, VOICE, music, limiter);
        Assert.That(limited, Is.TypeOf<RateLimitedResult>().With.Property(nameof(RateLimitedResult.RetryAfterSeconds)).GreaterThan(0));
        await music.Received(2 * ApiLimits.VOICE_CHANGES_PER_GUILD + 1).PlayAsync(GUILD, VOICE);
        mp.DidNotReceive().SetPaused(false);

        // Without a voice channel the player only moves on.
        Assert.That(Read(await MusicEndpoints.SkipAsync(GUILD, null, music, limiter)).Status, Is.EqualTo(200));
        mp.Received(1).Next();
    }

    [Test]
    public async Task AddTracks_JoinsOnlyWhenNeededAndMapsEachResult()
    {
        var mp = PlayerWithQueue();
        var music = MusicWithPlayer(mp);
        var channel = Substitute.For<ITextChannel>();
        channel.Id.Returns(VOICE);
        music.GetOrCreateMusicPlayerAsync(channel, false).Returns(mp);
        var first = mp.GetQueuedTracks().First();
        music.AddTrackAsync(mp, GUILD, VOICE, "song", QUEUER, false).Returns(new MusicAddResult(MusicAddStatus.Added, 1, first, 3));
        music.AddTrackAsync(mp, GUILD, null, "song", QUEUER, true).Returns(new MusicAddResult(MusicAddStatus.NotFound));
        music.AddTrackAsync(mp, GUILD, VOICE, "far", QUEUER, false).Returns(new MusicAddResult(MusicAddStatus.JoinFailed));
        music.AddYoutubePlaylistAsync(mp, GUILD, null, "mix", QUEUER).Returns(new MusicAddResult(MusicAddStatus.QueueFull));
        using var limiter = new VoiceChangeLimiter();
        var join = new MusicEndpoints.VoiceTarget(channel, true, null);
        var stay = new MusicEndpoints.VoiceTarget(channel, false, null);

        var added = await MusicEndpoints.AddTracksAsync(GUILD, join, QUEUER, new AddTracksRequest(" song "), music, limiter);
        Assert.That(Read(added).Value, Is.EqualTo(new AddTracksDto(1, MusicEndpoints.ToQueuedDto(first, 3))));

        var results = new[]
        {
            await MusicEndpoints.AddTracksAsync(GUILD, stay, QUEUER, new AddTracksRequest("song", Next: true), music, limiter),
            await MusicEndpoints.AddTracksAsync(GUILD, join, QUEUER, new AddTracksRequest("far"), music, limiter),
            await MusicEndpoints.AddTracksAsync(GUILD, stay, QUEUER, new AddTracksRequest("mix", Playlist: true), music, limiter),
            await MusicEndpoints.AddTracksAsync(GUILD, stay, QUEUER, new AddTracksRequest(" "), music, limiter),
            await MusicEndpoints.AddTracksAsync(GUILD, stay, QUEUER, new AddTracksRequest("mix", true, true), music, limiter),
            await MusicEndpoints.AddTracksAsync(GUILD, new MusicEndpoints.VoiceTarget(null, false, ApiErrors.ChannelRequired), QUEUER, new AddTracksRequest("song"), music, limiter)
        };
        Assert.That(results.Select(static r => (Read(r).Status, ErrorOf(r))),
            Is.EqualTo(new (int, string?)[]
            {
                (404, "track_not_found"), (502, "discord_unavailable"), (409, "limit_reached"), (400, "invalid_value"),
                (400, "invalid_value"), (400, "channel_required")
            }));

        // A full queue refuses before the join, so these use no permit.
        mp.IsQueueFull.Returns(true);
        Assert.That(ErrorOf(await MusicEndpoints.AddTracksAsync(GUILD, stay, QUEUER, new AddTracksRequest("song"), music, limiter)), Is.EqualTo("limit_reached"));
        for (var i = 0; i < ApiLimits.VOICE_CHANGES_PER_GUILD; i++)
            Assert.That(ErrorOf(await MusicEndpoints.AddTracksAsync(GUILD, join, QUEUER, new AddTracksRequest("song"), music, limiter)), Is.EqualTo("limit_reached"));
        await music.Received(1).AddTrackAsync(mp, GUILD, null, "song", QUEUER, true);
        mp.IsQueueFull.Returns(false);

        // Two joins used permits above; the rest of the minute's permits go, then the next join waits.
        for (var i = 2; i < ApiLimits.VOICE_CHANGES_PER_GUILD; i++)
            Assert.That(limiter.TryAcquire(GUILD), Is.Null);
        Assert.That(await MusicEndpoints.AddTracksAsync(GUILD, join, QUEUER, new AddTracksRequest("song"), music, limiter), Is.TypeOf<RateLimitedResult>());

        // PickVoiceChannel reads guild.GetVoiceChannel, which also returns a stage channel.
        Assert.That(typeof(Discord.WebSocket.SocketStageChannel).IsSubclassOf(typeof(Discord.WebSocket.SocketVoiceChannel)), Is.True);
    }

    [Test]
    public async Task LoadPlaylist_StartsInTheBackgroundAndReleasesTheLockOnEachRefusal()
    {
        using var db = new TestDbService();
        int full, empty;
        await using (var ctx = db.GetDbContext())
        {
            var songs = new[] { "dQw4w9WgXcQ", "9bZkp7q19f0", "kJQP7kiw5Fk" }
                .Select(static id => new PlaylistSong { Title = id, Query = "https://youtube.com/watch?v=" + id, Provider = "Youtube", ProviderType = MusicType.YouTube })
                .ToList();
            var withSongs = new MusicPlaylist { Name = "Chill", Author = "Kwoth", Songs = songs };
            var none = new MusicPlaylist { Name = "Empty", Author = "Kwoth" };
            ctx.AddRange(withSongs, none);
            await ctx.SaveChangesAsync();
            (full, empty) = (withSongs.Id, none.Id);
        }

        var mp = PlayerWithQueue();
        var music = MusicWithPlayer(mp);
        var channel = Substitute.For<ITextChannel>();
        channel.Id.Returns(VOICE);
        music.GetOrCreateMusicPlayerAsync(channel, false).Returns(mp);
        music.TryEnterPlaylistLoad().Returns(true);
        music.EnsureVoiceChannelAsync(GUILD, VOICE).Returns(false, true);
        using var limiter = new VoiceChangeLimiter();
        var join = new MusicEndpoints.VoiceTarget(channel, true, null);

        Task<IResult> Load(int id, MusicEndpoints.VoiceTarget target)
            => MusicEndpoints.LoadPlaylistAsync(GUILD, target, QUEUER, id, false, music, db, limiter);

        Assert.That(ErrorOf(await Load(9999, join)), Is.EqualTo("playlist_not_found"));
        Assert.That(ErrorOf(await Load(empty, join)), Is.EqualTo("invalid_value"));
        Assert.That(ErrorOf(await Load(full, join)), Is.EqualTo("discord_unavailable"));
        mp.IsQueueFull.Returns(true);
        Assert.That(ErrorOf(await Load(full, join)), Is.EqualTo("limit_reached"));
        mp.IsQueueFull.Returns(false);
        Assert.That(ErrorOf(await Load(full, new MusicEndpoints.VoiceTarget(null, false, ApiErrors.ChannelNotFound))), Is.EqualTo("channel_not_found"));
        music.Received(4).ExitPlaylistLoad();
        music.Received(4).TryEnterPlaylistLoad();

        var started = await Load(full, join);
        Assert.That((Read(started).Status, Read(started).Value), Is.EqualTo((202, (object)new LoadStartedDto(3))));
        music.Received(1).StartPlaylistLoad(mp,
            Arg.Is<IReadOnlyList<(string Query, MusicPlatform Platform)>>(static s =>
                s.Count == 3 && s[0].Query == "https://youtube.com/watch?v=dQw4w9WgXcQ" && s[2].Platform == MusicPlatform.Youtube),
            QUEUER);
        music.Received(4).ExitPlaylistLoad();

        music.TryEnterPlaylistLoad().Returns(false);
        Assert.That(ErrorOf(await Load(full, join)), Is.EqualTo("load_in_progress"));
        music.Received(4).ExitPlaylistLoad();
    }

    private const ulong GUILD = 1;
    private const ulong OTHER_GUILD = 2;
    private const ulong VOICE = 42;
    private const string QUEUER = "Kwoth";

    private static IMusicPlayer PlayerWithQueue()
    {
        var queue = new MusicQueue();
        queue.Enqueue(new RemoteTrackInfo("dQw4w9WgXcQ", "Song", "https://youtube.com/watch?v=dQw4w9WgXcQ", "", TimeSpan.FromSeconds(212), MusicPlatform.Youtube), QUEUER, out _);
        queue.Enqueue(new RemoteTrackInfo("9bZkp7q19f0", "Other", "https://youtube.com/watch?v=9bZkp7q19f0", "", TimeSpan.FromSeconds(100), MusicPlatform.Youtube), QUEUER, out _);
        var tracks = queue.List();
        var mp = Substitute.For<IMusicPlayer>();
        mp.GetQueuedTracks().Returns(tracks);
        mp.Volume.Returns(1f);
        mp.MaxQueueSize.Returns(500);
        return mp;
    }

    private static IMusicService MusicWithPlayer(IMusicPlayer mp)
    {
        var music = Substitute.For<IMusicService>();
        music.TryGetMusicPlayer(GUILD, out Arg.Any<IMusicPlayer>()).Returns(x =>
        {
            x[1] = mp;
            return true;
        });
        return music;
    }

    private static (int Status, object? Value) Read(IResult result)
        => ((result as IStatusCodeHttpResult)?.StatusCode ?? 200, (result as IValueHttpResult)?.Value);

    private static string? ErrorOf(IResult result)
        => result is RateLimitedResult ? "rate_limited" : (Read(result).Value as ErrorDto)?.Error;
}
