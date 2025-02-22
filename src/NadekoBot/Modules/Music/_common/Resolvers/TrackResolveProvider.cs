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

    public Task<ITrackInfo?> QuerySongAsync(string query, MusicPlatform? forcePlatform)
    {
        switch (forcePlatform)
        {
            case MusicPlatform.Radio:
                return _radioResolver.ResolveByQueryAsync(query);
            case MusicPlatform.Youtube:
                return _ytResolver.GetYoutubeResolver().ResolveByQueryAsync(query);
            case MusicPlatform.Local:
                return _localResolver.ResolveByQueryAsync(query);
            case null:
                var match = YoutubeHelpers.YtVideoIdRegex.Match(query);

                if (match.Success)
                    return _ytResolver.GetYoutubeResolver().ResolveByIdAsync(match.Groups["id"].Value);

                if (Uri.TryCreate(query, UriKind.Absolute, out var uri) && uri.IsFile)
                    return _localResolver.ResolveByQueryAsync(uri.AbsolutePath);

                if (IsRadioLink(query))
                    return _radioResolver.ResolveByQueryAsync(query);

                return _ytResolver.GetYoutubeResolver().ResolveByQueryAsync(query, false);
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