using NadekoBot.Modules.Music;

namespace NadekoBot.Modules.Searches.Youtube;

public sealed class ServiceYtSearchService(MusicServiceClient service) : IYoutubeSearchService, INService
{
    private const int MAX_RESULTS = 5;

    public async Task<VideoInfo[]?> SearchAsync(string query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var tracks = await service.SearchAsync(query, MAX_RESULTS);
        if (tracks.Length == 0)
            return null;

        var videos = new VideoInfo[tracks.Length];
        for (var i = 0; i < tracks.Length; i++)
            videos[i] = new VideoInfo(tracks[i].Id!);

        return videos;
    }
}
