#nullable disable
using System.Text;
using System.Text.RegularExpressions;

namespace NadekoBot.Modules.Music.Resolvers;

public class RadioResolver : IRadioResolver
{
    private const int MAX_PLAYLIST_BYTES = 64 * 1024;

    private readonly Regex _plsRegex = new(@"File1=(?<url>.*?)\n", RegexOptions.Compiled);
    private readonly Regex _m3URegex = new(@"(?<url>^[^#].*)", RegexOptions.Compiled | RegexOptions.Multiline);
    private readonly Regex _asxRegex = new(@"<ref href=""(?<url>.*?)""", RegexOptions.Compiled);
    private readonly Regex _xspfRegex = new(@"<location>(?<url>.*?)</location>", RegexOptions.Compiled);

    private readonly IHttpClientFactory _httpFactory;

    public RadioResolver(IHttpClientFactory httpFactory)
        => _httpFactory = httpFactory;

    public Task<ITrackInfo> ResolveByQueryAsync(string query)
        => ResolveByQueryAsync(query, false);

    public async Task<ITrackInfo> ResolveByQueryAsync(string query, bool allowLocalNetwork)
    {
        if (IsRadioLink(query))
            query = await HandleStreamContainersInternalAsync(query, allowLocalNetwork);

        if (string.IsNullOrWhiteSpace(query))
            return null;

        if (allowLocalNetwork
                ? !Uri.TryCreate(query, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                : !UrlExtensions.IsPublicUrl(query))
            return null;

        return new SimpleTrackInfo(query.TrimTo(50),
            query,
            "https://cdn.discordapp.com/attachments/155726317222887425/261850925063340032/1482522097_radio.png",
            TimeSpan.MaxValue,
            MusicPlatform.Radio,
            query);
    }

    public static bool IsRadioLink(string query)
        => (query.StartsWith("http", StringComparison.InvariantCulture)
            || query.StartsWith("ww", StringComparison.InvariantCulture))
           && (query.Contains(".pls") || query.Contains(".m3u") || query.Contains(".asx") || query.Contains(".xspf"));

    private async Task<string> HandleStreamContainersInternalAsync(string query, bool allowLocalNetwork)
    {
        if (!allowLocalNetwork && !UrlExtensions.IsPublicUrl(query))
            return query;

        string file = null;
        try
        {
            using var http = _httpFactory.CreateClient(allowLocalNetwork
                ? ExternalHttp.OWNER_CLIENT_NAME
                : ExternalHttp.CLIENT_NAME);
            http.Timeout = TimeSpan.FromSeconds(10);
            var bytes = await http.GetBytesLimitedAsync(query, MAX_PLAYLIST_BYTES);
            if (bytes is null)
                return query;

            file = Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return query;
        }

        if (query.Contains(".pls"))
        {
            try
            {
                var m = _plsRegex.Match(file);
                var res = m.Groups["url"]?.ToString();
                return res?.Trim();
            }
            catch
            {
                Log.Warning("Failed reading .pls:\n{PlsFile}", file);
                return null;
            }
        }

        if (query.Contains(".m3u"))
        {
            try
            {
                var m = _m3URegex.Match(file);
                var res = m.Groups["url"].ToString();
                return res.Trim();
            }
            catch
            {
                Log.Warning("Failed reading .m3u:\n{M3uFile}", file);
                return null;
            }
        }

        if (query.Contains(".asx"))
        {
            try
            {
                var m = _asxRegex.Match(file);
                var res = m.Groups["url"].ToString();
                return res.Trim();
            }
            catch
            {
                Log.Warning("Failed reading .asx:\n{AsxFile}", file);
                return null;
            }
        }

        if (query.Contains(".xspf"))
        {
            try
            {
                var m = _xspfRegex.Match(file);
                var res = m.Groups["url"].ToString();
                return res.Trim();
            }
            catch
            {
                Log.Warning("Failed reading .xspf:\n{XspfFile}", file);
                return null;
            }
        }

        return query;
    }
}
