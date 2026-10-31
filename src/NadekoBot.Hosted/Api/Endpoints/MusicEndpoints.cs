using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Music;
using NadekoBot.Modules.Music.Services;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace NadekoBot.Hosted.Endpoints;

// The routes act as the bot owner, who is not in Discord, so no route checks the voice channel of a member.
// Each handler reads the gateway cache, then calls a public method which tests can run without a guild.
public static class MusicEndpoints
{
    public const int MAX_QUERY_LENGTH = 200;
    public const int DEFAULT_SEARCH_LIMIT = 5;
    public const int MAX_SEARCH_LIMIT = 5;

    private const string REPEAT_NONE = "none";
    private const string REPEAT_TRACK = "track";
    private const string REPEAT_QUEUE = "queue";

    private const string PLATFORM_YOUTUBE = "youtube";
    private const string PLATFORM_RADIO = "radio";
    private const string PLATFORM_LOCAL = "local";

    private const float VOLUME_SCALE = 100f;

    public static RouteGroupBuilder MapMusicEndpoints(this RouteGroupBuilder owner)
    {
        owner.MapGet("/music/search", Search);
        owner.MapPlaylistEndpoints();
        return owner;
    }

    public static RouteGroupBuilder MapGuildMusicEndpoints(this RouteGroupBuilder guild)
    {
        guild.MapGet("/music", GetState);
        guild.MapPut("/music", UpdatePlayer);
        guild.MapDelete("/music", Destroy);
        guild.MapPost("/music/tracks", AddTracks);
        guild.MapDelete("/music/tracks/{index:int}", RemoveTrack);
        guild.MapPost("/music/tracks/move", MoveTrack);
        guild.MapPost("/music/play", PlayTrack);
        guild.MapPost("/music/skip", Skip);
        guild.MapPost("/music/shuffle", Shuffle);
        guild.MapPost("/music/playlists/{id:int}/load", LoadPlaylist);
        return guild;
    }

    private static async Task<IResult> Search(string? q, int? limit, IMusicService music)
    {
        var count = limit ?? DEFAULT_SEARCH_LIMIT;
        if (!ApiLimits.IsText(q, MAX_QUERY_LENGTH) || count is < 1 or > MAX_SEARCH_LIMIT)
            return ApiErrors.InvalidValue;

        var result = await music.SearchTracksAsync(q.Trim(), count);
        if (result.Failed)
            return ApiErrors.MusicUnavailable;

        var found = result.Tracks;
        var tracks = new MusicTrackDto[Math.Min(found.Count, count)];
        for (var i = 0; i < tracks.Length; i++)
        {
            var t = found[i];
            tracks[i] = new(t.Id, t.Title, t.Url, NullIfEmpty(t.Thumbnail), DurationSeconds(t.Duration));
        }

        return Results.Json(new MusicSearchDto(tracks), ApiJsonContext.Default.MusicSearchDto);
    }

    private static Task<IResult> GetState(GuildRef guild, IMusicService music)
        => GetStateAsync(guild.Id, BotVoiceChannelId(guild.Guild), music);

    public static async Task<IResult> GetStateAsync(ulong guildId, ulong? voiceChannelId, IMusicService music)
    {
        if (music.TryGetMusicPlayer(guildId, out var mp))
            return StateResult(voiceChannelId, mp);

        var saved = await music.GetSavedSettingsAsync(guildId);
        var dto = new MusicStateDto(
            voiceChannelId?.ToString(),
            false,
            false,
            false,
            RepeatName(saved.PlayerRepeat),
            saved.AutoPlay,
            Math.Clamp(saved.Volume, 0, (int)VOLUME_SCALE),
            null,
            music.MaxQueueSize,
            []);
        return Results.Json(dto, ApiJsonContext.Default.MusicStateDto);
    }

    private static Task<IResult> AddTracks(
        GuildRef guild,
        AddTracksRequest body,
        IMusicService music,
        DiscordSocketClient client,
        VoiceChangeLimiter limiter)
    {
        if (!TryGetQueuer(guild.Guild, client, body.By, out var queuer))
            return Task.FromResult(ApiErrors.InvalidValue);

        return AddTracksAsync(guild.Id, PickVoiceChannel(guild.Guild, body.VoiceChannelId), queuer, body, music, limiter);
    }

    public static async Task<IResult> AddTracksAsync(
        ulong guildId,
        VoiceTarget target,
        string queuer,
        AddTracksRequest body,
        IMusicService music,
        VoiceChangeLimiter limiter)
    {
        var playlist = body.Playlist ?? false;
        var next = body.Next ?? false;
        if (!ApiLimits.IsText(body.Query, MAX_QUERY_LENGTH) || (playlist && next))
            return ApiErrors.InvalidValue;

        if (target.Error is { } error)
            return error;

        // A refused add must not use a voice permit, and a new player does not join.
        var mp = await music.GetOrCreateMusicPlayerAsync(target.Channel!, followChannel: false);
        if (mp.IsQueueFull)
            return ApiErrors.LimitReached;

        if (target.Join && limiter.TryAcquire(guildId) is { } retryAfter)
            return new RateLimitedResult(retryAfter);

        var joinId = target.Join ? target.Channel!.Id : (ulong?)null;
        var query = body.Query.Trim();
        var result = playlist
            ? await music.AddYoutubePlaylistAsync(mp, guildId, joinId, query, queuer)
            : await music.AddTrackAsync(mp, guildId, joinId, query, queuer, next);

        return result.Status switch
        {
            MusicAddStatus.JoinFailed => ApiErrors.DiscordUnavailable,
            MusicAddStatus.NotFound => ApiErrors.TrackNotFound,
            MusicAddStatus.QueueFull => ApiErrors.LimitReached,
            _ => Results.Json(
                new AddTracksDto(result.Added,
                    result.First is { } first ? ToQueuedDto(first, result.FirstIndex) : null),
                ApiJsonContext.Default.AddTracksDto)
        };
    }

    private static Task<IResult> LoadPlaylist(
        GuildRef guild,
        int id,
        LoadPlaylistRequest? body,
        IMusicService music,
        DbService db,
        DiscordSocketClient client,
        VoiceChangeLimiter limiter)
    {
        if (!TryGetQueuer(guild.Guild, client, body?.By, out var queuer))
            return Task.FromResult(ApiErrors.InvalidValue);

        return LoadPlaylistAsync(guild.Id,
            PickVoiceChannel(guild.Guild, body?.VoiceChannelId),
            queuer,
            id,
            body?.Shuffle is true,
            music,
            db,
            limiter);
    }

    public static async Task<IResult> LoadPlaylistAsync(
        ulong guildId,
        VoiceTarget target,
        string queuer,
        int id,
        bool shuffle,
        IMusicService music,
        DbService db,
        VoiceChangeLimiter limiter)
    {
        if (target.Error is { } error)
            return error;

        if (!music.TryEnterPlaylistLoad())
            return ApiErrors.LoadInProgress;

        var started = false;
        try
        {
            var songs = await PlaylistEndpoints.ReadSongQueriesAsync(db, id);
            if (songs is null)
                return ApiErrors.PlaylistNotFound;

            if (songs.Length == 0)
                return ApiErrors.InvalidValue;

            var mp = await music.GetOrCreateMusicPlayerAsync(target.Channel!, followChannel: false);
            if (mp.IsQueueFull)
                return ApiErrors.LimitReached;

            if (target.Join && limiter.TryAcquire(guildId) is { } retryAfter)
                return new RateLimitedResult(retryAfter);

            if (target.Join && !await music.EnsureVoiceChannelAsync(guildId, target.Channel!.Id))
                return ApiErrors.DiscordUnavailable;

            music.AttachProxy(guildId);
            if (shuffle)
                RandomNumberGenerator.Shuffle(songs.AsSpan());

            music.StartPlaylistLoad(mp, songs, queuer);
            started = true;
            return Results.Json(new LoadStartedDto(songs.Length),
                ApiJsonContext.Default.LoadStartedDto,
                statusCode: StatusCodes.Status202Accepted);
        }
        finally
        {
            if (!started)
                music.ExitPlaylistLoad();
        }
    }

    private static IResult RemoveTrack(GuildRef guild, int index, IMusicService music)
        => RemoveTrackAt(guild.Id, BotVoiceChannelId(guild.Guild), index, music);

    public static IResult RemoveTrackAt(ulong guildId, ulong? voiceChannelId, int index, IMusicService music)
    {
        if (!music.TryGetMusicPlayer(guildId, out var mp))
            return ApiErrors.NoPlayer;

        return mp.TryRemoveTrackAt(index, out _)
            ? StateResult(voiceChannelId, mp)
            : ApiErrors.InvalidValue;
    }

    private static IResult MoveTrack(GuildRef guild, MoveTrackRequest body, IMusicService music)
        => MoveTrackTo(guild.Id, BotVoiceChannelId(guild.Guild), body, music);

    public static IResult MoveTrackTo(ulong guildId, ulong? voiceChannelId, MoveTrackRequest body, IMusicService music)
    {
        if (!music.TryGetMusicPlayer(guildId, out var mp))
            return ApiErrors.NoPlayer;

        if (body.From < 0 || body.To < 0 || body.From == body.To || mp.MoveTrack(body.From, body.To) is null)
            return ApiErrors.InvalidValue;

        return StateResult(voiceChannelId, mp);
    }

    private static IResult PlayTrack(GuildRef guild, PlayTrackRequest body, IMusicService music)
        => PlayTrackAt(guild.Id, BotVoiceChannelId(guild.Guild), body, music);

    public static IResult PlayTrackAt(ulong guildId, ulong? voiceChannelId, PlayTrackRequest body, IMusicService music)
    {
        if (!music.TryGetMusicPlayer(guildId, out var mp))
            return ApiErrors.NoPlayer;

        if (!mp.MoveTo(body.Index))
            return ApiErrors.InvalidValue;

        music.AttachProxy(guildId);
        return StateResult(voiceChannelId, mp);
    }

    private static Task<IResult> Skip(GuildRef guild, IMusicService music, VoiceChangeLimiter limiter)
        => SkipAsync(guild.Id, BotVoiceChannelId(guild.Guild), music, limiter);

    public static async Task<IResult> SkipAsync(
        ulong guildId,
        ulong? voiceChannelId,
        IMusicService music,
        VoiceChangeLimiter limiter)
    {
        if (!music.TryGetMusicPlayer(guildId, out var mp))
            return ApiErrors.NoPlayer;

        if (await NextInternalAsync(guildId, voiceChannelId, music, mp, limiter) is { } limited)
            return limited;

        return StateResult(voiceChannelId, mp);
    }

    private static IResult Shuffle(GuildRef guild, IMusicService music)
        => ShuffleQueue(guild.Id, BotVoiceChannelId(guild.Guild), music);

    public static IResult ShuffleQueue(ulong guildId, ulong? voiceChannelId, IMusicService music)
    {
        if (!music.TryGetMusicPlayer(guildId, out var mp))
            return ApiErrors.NoPlayer;

        mp.ShuffleQueue();
        return StateResult(voiceChannelId, mp);
    }

    private static Task<IResult> UpdatePlayer(
        GuildRef guild,
        UpdateMusicRequest body,
        IMusicService music,
        VoiceChangeLimiter limiter)
        => UpdatePlayerAsync(guild.Id, BotVoiceChannelId(guild.Guild), body, music, limiter);

    public static async Task<IResult> UpdatePlayerAsync(
        ulong guildId,
        ulong? voiceChannelId,
        UpdateMusicRequest body,
        IMusicService music,
        VoiceChangeLimiter limiter)
    {
        PlayerRepeatType? repeat = null;
        if (body.Repeat is { } repeatName)
        {
            if (!TryParseRepeat(repeatName, out var parsed))
                return ApiErrors.InvalidValue;
            repeat = parsed;
        }

        if (body.Volume is < 0 or > (int)VOLUME_SCALE)
            return ApiErrors.InvalidValue;

        if (!music.TryGetMusicPlayer(guildId, out var mp))
            return ApiErrors.NoPlayer;

        // The permit comes before any write, so a refused request changes nothing.
        var resume = body.Paused is false && mp.IsStopped;
        if (resume && NeedsRejoinPermitInternal(voiceChannelId, mp) && limiter.TryAcquire(guildId) is { } retryAfter)
            return new RateLimitedResult(retryAfter);

        if (repeat is { } r)
            await music.SetRepeatAsync(guildId, r);

        if (body.Volume is { } volume)
            await music.SetVolumeAsync(guildId, volume);

        if (resume)
            await PlayNextInternalAsync(guildId, voiceChannelId, music, mp);
        else if (body.Paused is { } paused)
            mp.SetPaused(paused);

        return StateResult(voiceChannelId, mp);
    }

    private static Task<IResult> Destroy(GuildRef guild, IMusicService music, VoiceChangeLimiter limiter)
        => DestroyAsync(guild.Id, BotVoiceChannelId(guild.Guild), music, limiter);

    public static async Task<IResult> DestroyAsync(
        ulong guildId,
        ulong? voiceChannelId,
        IMusicService music,
        VoiceChangeLimiter limiter)
    {
        if (voiceChannelId is null && !music.TryGetMusicPlayer(guildId, out _))
            return Results.NoContent();

        if (limiter.TryAcquire(guildId) is { } retryAfter)
            return new RateLimitedResult(retryAfter);

        await music.LeaveVoiceChannelAsync(guildId);
        return Results.NoContent();
    }

    // Null when the player moved on, else the rate limit answer.
    private static async Task<IResult?> NextInternalAsync(
        ulong guildId,
        ulong? voiceChannelId,
        IMusicService music,
        IMusicPlayer mp,
        VoiceChangeLimiter limiter)
    {
        if (NeedsRejoinPermitInternal(voiceChannelId, mp) && limiter.TryAcquire(guildId) is { } retryAfter)
            return new RateLimitedResult(retryAfter);

        await PlayNextInternalAsync(guildId, voiceChannelId, music, mp);
        return null;
    }

    // MusicService.PlayAsync joins again when a stopped player has no voice connection, so a stopped player costs a permit.
    private static bool NeedsRejoinPermitInternal(ulong? voiceChannelId, IMusicPlayer mp)
        => voiceChannelId is not null && mp.IsStopped;

    // As .play without a query: a stopped player plays the next track.
    private static Task PlayNextInternalAsync(ulong guildId, ulong? voiceChannelId, IMusicService music, IMusicPlayer mp)
    {
        if (voiceChannelId is { } channelId)
            return music.PlayAsync(guildId, channelId);

        mp.Next();
        return Task.CompletedTask;
    }

    // Channel is also the default channel of a new player's messages: a voice channel has a text chat.
    public readonly record struct VoiceTarget(ITextChannel? Channel, bool Join, IResult? Error);

    // The bot's own voice channel wins, so a second dashboard tab cannot move the bot away from its listeners.
    public static VoiceTarget PickVoiceChannel(SocketGuild guild, string? requestedId)
    {
        if (guild.CurrentUser is not { } me)
            return new(null, false, ApiErrors.NotReady);

        if (me.VoiceChannel is { } current)
            return new(current, false, null);

        if (requestedId is null)
            return new(null, false, ApiErrors.ChannelRequired);

        if (!ApiLimits.TryParseId(requestedId, out var id) || guild.GetVoiceChannel(id) is not { } channel)
            return new(null, false, ApiErrors.ChannelNotFound);

        var perms = me.GetPermissions(channel);
        if (!perms.ViewChannel || !perms.Connect || !perms.Speak)
            return new(null, false, ApiErrors.MissingPermissions);

        return new(channel, true, null);
    }

    // A dashboard user who is not a member of the server shows as the bot. An empty value is no value.
    private static bool TryGetQueuer(SocketGuild guild, DiscordSocketClient client, string? by, out string queuer)
    {
        queuer = client.CurrentUser?.ToString() ?? string.Empty;
        if (string.IsNullOrEmpty(by))
            return true;

        if (!ApiLimits.TryParseId(by, out var userId))
            return false;

        if (guild.GetUser(userId) is { } user)
            queuer = user.ToString();

        return true;
    }

    private static IResult StateResult(ulong? voiceChannelId, IMusicPlayer mp)
        => Results.Json(ToStateDto(voiceChannelId?.ToString(), mp), ApiJsonContext.Default.MusicStateDto);

    private static ulong? BotVoiceChannelId(SocketGuild guild)
        => guild.CurrentUser?.VoiceChannel?.Id;

    public static MusicStateDto ToStateDto(string? voiceChannelId, IMusicPlayer mp)
    {
        var queued = mp.GetQueuedTracks();
        var tracks = new QueuedTrackDto[queued.Count];
        var i = 0;
        foreach (var track in queued)
        {
            if (i == tracks.Length)
                break;

            tracks[i] = ToQueuedDto(track, i);
            i++;
        }

        var current = mp.GetCurrentTrack(out var currentIndex);
        return new(
            voiceChannelId,
            true,
            mp.IsPaused,
            mp.IsStopped,
            RepeatName(mp.Repeat),
            mp.AutoPlay,
            (int)MathF.Round(mp.Volume * VOLUME_SCALE),
            current is null ? null : currentIndex,
            mp.MaxQueueSize,
            tracks);
    }

    public static QueuedTrackDto ToQueuedDto(IQueuedTrackInfo track, int index)
        => new(
            index,
            track.TrackInfo.Id,
            track.Title,
            track.Url,
            NullIfEmpty(track.Thumbnail),
            DurationSeconds(track.Duration),
            PlatformName(track.Platform),
            track.Queuer);

    // A radio stream has TimeSpan.MaxValue as its length.
    public static int DurationSeconds(TimeSpan duration)
        => duration <= TimeSpan.Zero || duration == TimeSpan.MaxValue
            ? 0
            : (int)Math.Min(duration.TotalSeconds, int.MaxValue);

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string PlatformName(MusicPlatform platform)
        => platform switch
        {
            MusicPlatform.Radio => PLATFORM_RADIO,
            MusicPlatform.Local => PLATFORM_LOCAL,
            _ => PLATFORM_YOUTUBE
        };

    private static string RepeatName(PlayerRepeatType repeat)
        => repeat switch
        {
            PlayerRepeatType.None => REPEAT_NONE,
            PlayerRepeatType.Track => REPEAT_TRACK,
            _ => REPEAT_QUEUE
        };

    public static bool TryParseRepeat(string name, [NotNullWhen(true)] out PlayerRepeatType? repeat)
    {
        repeat = name switch
        {
            REPEAT_NONE => PlayerRepeatType.None,
            REPEAT_TRACK => PlayerRepeatType.Track,
            REPEAT_QUEUE => PlayerRepeatType.Queue,
            _ => null
        };
        return repeat is not null;
    }
}
