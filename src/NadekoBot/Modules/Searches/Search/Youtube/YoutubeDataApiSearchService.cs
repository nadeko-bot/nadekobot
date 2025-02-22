namespace NadekoBot.Modules.Searches.Youtube;

public sealed class YoutubeDataApiSearchService : IYoutubeSearchService, INService
{
    private readonly IGoogleApiService _gapi;

    public YoutubeDataApiSearchService(IGoogleApiService gapi)
    {
        _gapi = gapi;
    }

    public async Task<VideoInfo[]?> SearchAsync(string query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var results = await _gapi.GetVideoLinksByKeywordAsync(query);
        
        if(results.Count == 0)
            return null;
        
        return results.Map(r => new VideoInfo()
        {
            Url = r
        });
    }
}