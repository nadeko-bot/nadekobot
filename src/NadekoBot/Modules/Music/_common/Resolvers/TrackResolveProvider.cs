using NadekoBot.Modules.Music.Resolvers;

namespace NadekoBot.Modules.Music;

public sealed class TrackResolveProvider : ITrackResolveProvider
{
    private readonly IYoutubeResolverFactory _ytResolver;
    private readonly ILocalTrackResolver _localResolver;
    private readonly IRadioResolver _radioResolver;

    public TrackResolveProvider(
        IYoutubeResolverFactory ytResolver,
        ILocalTrackResolver localResolver,
        IRadioResolver radioResolver)
    {
        _ytResolver = ytResolver;
        _localResolver = localResolver;
        _radioResolver = radioResolver;
    }

    public Task<ITrackInfo?> QuerySongAsync(string query, MusicPlatform? forcePlatform, bool allowLocalNetwork)
    {
        switch (forcePlatform)
        {
            case MusicPlatform.Radio:
                return _radioResolver.ResolveByQueryAsync(query, allowLocalNetwork);
            case MusicPlatform.Youtube:
                return _ytResolver.GetYoutubeResolver().ResolveByQueryAsync(query, true, allowLocalNetwork);
            case MusicPlatform.Local:
                return allowLocalNetwork
                    ? _localResolver.ResolveByQueryAsync(query)
                    : Task.FromResult<ITrackInfo?>(null);
            case null:
                var match = YoutubeHelpers.YtVideoIdRegex.Match(query);

                if (match.Success)
                    return _ytResolver.GetYoutubeResolver().ResolveByIdAsync(match.Groups["id"].Value);

                if (IsRadioLink(query))
                    return _radioResolver.ResolveByQueryAsync(query, allowLocalNetwork);

                return _ytResolver.GetYoutubeResolver().ResolveByQueryAsync(query, false, allowLocalNetwork);
            default:
                Log.Error("Unsupported platform: {MusicPlatform}", forcePlatform);
                return Task.FromResult<ITrackInfo?>(null);
        }
    }

    public static bool IsRadioLink(string query)
        => (query.StartsWith("http", StringComparison.InvariantCulture)
            || query.StartsWith("ww", StringComparison.InvariantCulture))
           && (query.Contains(".pls") || query.Contains(".m3u") || query.Contains(".asx") || query.Contains(".xspf"));
}