using Microsoft.Extensions.DependencyInjection;
using NadekoBot.Modules.Searches;
using NadekoBot.Modules.Searches.Services;

namespace NadekoBot.Modules.Music.Resolvers;

public interface IYoutubeResolverFactory
{
    IYoutubeResolver GetYoutubeResolver();
}

public sealed class YoutubeResolverFactory : IYoutubeResolverFactory
{
    private readonly SearchesConfigService _ss;
    private readonly IServiceProvider _services;

    public YoutubeResolverFactory(SearchesConfigService ss, IServiceProvider services)
    {
        _ss = ss;
        _services = services;
    }

    public IYoutubeResolver GetYoutubeResolver()
    {
        var conf = _ss.Data;
        if (conf.YtProvider == YoutubeSearcher.Invidious)
        {
            return _services.GetRequiredService<InvidiousYoutubeResolver>();
        }

        return _services.GetRequiredService<YtdlYoutubeResolver>();
    }
}