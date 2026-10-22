using System.Text.RegularExpressions;

namespace NadekoBot.Modules.Music;

public sealed class ServiceYoutubeResolver(MusicServiceClient service) : IYoutubeResolver
{
    private static readonly Regex _playlistIdRegex = new(@"[?&]list=(?<id>[\w\-]{12,64})", RegexOptions.Compiled);

    public MusicServiceClient Service
        => service;

    public static ITrackInfo ToTrackInfo(MusicServiceTrack track)
        => new RemoteTrackInfo(
            track.Id!,
            track.Title!,
            $"https://youtube.com/watch?v={track.Id}",
            track.Thumbnail ?? string.Empty,
            TimeSpan.FromSeconds(Math.Max(0, track.DurationSec)),
            MusicPlatform.Youtube);

    public async Task<ITrackInfo?> ResolveByIdAsync(string id)
        => await service.GetTrackAsync(id.Trim()) is { } track ? ToTrackInfo(track) : null;

    public Task<ITrackInfo?> ResolveByQueryAsync(string query)
        => ResolveByQueryAsync(query, true, false);

    // The service only plays YouTube, so a local link of the owner never reaches it.
    public async Task<ITrackInfo?> ResolveByQueryAsync(string query, bool tryExtractingId, bool allowLocalNetwork)
    {
        if (tryExtractingId)
        {
            var match = YoutubeHelpers.YtVideoIdRegex.Match(query);
            if (match.Success)
                return await ResolveByIdAsync(match.Groups["id"].Value);
        }

        var tracks = await service.SearchAsync(query, 1);
        return tracks.Length == 0 ? null : ToTrackInfo(tracks[0]);
    }

    public async IAsyncEnumerable<ITrackInfo> ResolveTracksFromPlaylistAsync(string query)
    {
        var match = _playlistIdRegex.Match(query);
        var playlistId = match.Success
            ? match.Groups["id"].Value
            : (await service.SearchPlaylistAsync(query))?.Id;

        if (playlistId is null || await service.GetPlaylistAsync(playlistId) is not { Tracks: { } tracks })
            yield break;

        foreach (var track in tracks)
            yield return ToTrackInfo(track);
    }

    public Task<string?> GetStreamUrl(string videoId)
        => Task.FromResult(service.GetAudioUrl(videoId));

}
