using Microsoft.Extensions.DependencyInjection;
using NadekoBot.Modules.Searches;

namespace NadekoBot.Modules.Music.Resolvers;

public interface IYoutubeResolverFactory
{
    IYoutubeResolver GetYoutubeResolver();
}

public sealed class YoutubeResolverFactory : IYoutubeResolverFactory
{
    private readonly SearchesConfigService _ss;
    private readonly IServiceProvider _services;
    private readonly BotPolicy _policy;

    public YoutubeResolverFactory(SearchesConfigService ss, IServiceProvider services, BotPolicy policy)
    {
        _ss = ss;
        _services = services;
        _policy = policy;
    }

    public IYoutubeResolver GetYoutubeResolver()
    {
        return SearchEngineChoice.Yt(_ss.Data, _policy) switch
        {
            YoutubeSearcher.Invidious => _services.GetRequiredService<InvidiousYoutubeResolver>(),
            YoutubeSearcher.Service => _services.GetRequiredService<ServiceYoutubeResolver>(),
            _ => _services.GetRequiredService<YtdlYoutubeResolver>()
        };
    }
}