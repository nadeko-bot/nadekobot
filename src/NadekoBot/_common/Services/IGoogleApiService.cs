#nullable disable

namespace NadekoBot.Services;

public interface IGoogleApiService
{
    IReadOnlyDictionary<string, string> Languages { get; }

    Task<IReadOnlyList<string>> GetVideoLinksByKeywordAsync(string keywords, int count = 1);
    Task<IEnumerable<(string Name, string Id, string Url, string Thumbnail)>> GetVideoInfosByKeywordAsync(string keywords, int count = 1);
    Task<IEnumerable<string>> GetPlaylistIdsByKeywordsAsync(string keywords, int count = 1);
    Task<IEnumerable<string>> GetRelatedVideosAsync(string id, int count = 1, string user = null);
    Task<IEnumerable<string>> GetPlaylistTracksAsync(string playlistId, int count = 50);
    Task<IReadOnlyDictionary<string, TimeSpan>> GetVideoDurationsAsync(IEnumerable<string> videoIds);
    Task<string> Translate(string sourceText, string sourceLanguage, string targetLanguage);

    Task<string> ShortenUrl(string url);
    Task<string> ShortenUrl(Uri url);
}