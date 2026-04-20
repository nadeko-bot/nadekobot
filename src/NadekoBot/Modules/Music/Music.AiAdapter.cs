using NadekoBot.AiAgent;
using NadekoBot.Modules.Music.Services;
using NadekoBot.Modules.Utility.AiAgent;

namespace NadekoBot.Modules.Music;

public sealed class MusicAiAdapter(IMusicService music) : IAiToolGroup, INService
{
    public string GroupName => "music";
    public string GroupDescription => "Music player: currently playing track, queue, and music settings.";

    [AiTool("get_now_playing", "Returns the currently playing track in this server, or null if nothing is playing.")]
    public Task<NowPlayingDto> GetNowPlaying(AiToolContext ctx)
    {
        if (!music.TryGetMusicPlayer(ctx.Guild.Id, out var mp))
            return Task.FromResult(new NowPlayingDto(false, null, null, null));

        var current = mp.GetCurrentTrack(out var index);
        if (current is null)
            return Task.FromResult(new NowPlayingDto(true, null, null, null));

        var state = mp.IsPaused ? "paused" : mp.IsStopped ? "stopped" : "playing";
        var track = new MusicTrackDto(
            index,
            current.Title,
            current.Url,
            current.Duration,
            current.Platform.ToString(),
            current.Queuer);

        return Task.FromResult(new NowPlayingDto(true, track, state, mp.Repeat.ToString()));
    }

    [AiTool("get_music_queue", "Returns the current music queue in this server.")]
    public Task<MusicQueueDto> GetMusicQueue(
        AiToolContext ctx,
        [AiParam("Maximum number of tracks to return, max 50")] int top = 25)
    {
        top = Math.Clamp(top, 1, 50);

        if (!music.TryGetMusicPlayer(ctx.Guild.Id, out var mp))
            return Task.FromResult(new MusicQueueDto(false, 0, -1, []));

        var tracks = mp.GetQueuedTracks();
        var entries = new List<MusicTrackDto>(Math.Min(tracks.Count, top));

        var idx = 0;
        foreach (var t in tracks)
        {
            if (entries.Count >= top)
                break;

            entries.Add(new(idx, t.Title, t.Url, t.Duration, t.Platform.ToString(), t.Queuer));
            idx++;
        }

        return Task.FromResult(new MusicQueueDto(true, tracks.Count, mp.CurrentIndex, entries));
    }

    [AiTool("get_music_settings", "Returns this server's music settings: quality, repeat mode, auto-disconnect, etc.")]
    public async Task<MusicSettingsDto> GetMusicSettings(AiToolContext ctx)
    {
        var quality = await music.GetMusicQualityAsync(ctx.Guild.Id);
        float? volume = null;
        string? repeat = null;
        bool? autoPlay = null;
        if (music.TryGetMusicPlayer(ctx.Guild.Id, out var mp))
        {
            volume = mp.Volume;
            repeat = mp.Repeat.ToString();
            autoPlay = mp.AutoPlay;
        }

        return new(quality.ToString(), volume, repeat, autoPlay);
    }
}

public sealed record NowPlayingDto(bool PlayerActive, MusicTrackDto? Track, string? State, string? Repeat);

public sealed record MusicQueueDto(bool PlayerActive, int TotalCount, int CurrentIndex, List<MusicTrackDto> Tracks);

public readonly record struct MusicTrackDto(int Index, string Title, string Url, TimeSpan Duration, string Platform, string? Queuer);

public readonly record struct MusicSettingsDto(string Quality, float? Volume, string? Repeat, bool? AutoPlay);
