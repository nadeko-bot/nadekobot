using System.Text.RegularExpressions;

namespace NadekoBot.Modules.Music;

public interface IYoutubeResolver : IPlatformQueryResolver
{
    public Task<ITrackInfo?> ResolveByIdAsync(string id);
    IAsyncEnumerable<ITrackInfo> ResolveTracksFromPlaylistAsync(string query);
    Task<ITrackInfo?> ResolveByQueryAsync(string query, bool tryExtractingId);
    Task<string?> GetStreamUrl(string query);
}