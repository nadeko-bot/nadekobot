using NadekoBot.Modules.Music;

namespace NadekoBot.Modules.Searches;

// A managed bot has no API keys or self-hosted instances, so it uses the engines which need neither, or the
// manager's music service.
public static class SearchEngineChoice
{
    public const WebSearchEngine MANAGED_WEB = WebSearchEngine.Google_Scrape;
    public const ImgSearchEngine MANAGED_IMG = ImgSearchEngine.DuckDuckGo;
    public const YoutubeSearcher MANAGED_YT = YoutubeSearcher.Ytdlp;

    public static WebSearchEngine Web(SearchesConfig config, BotPolicy policy)
        => policy.Managed ? MANAGED_WEB : config.WebSearchEngine;

    public static ImgSearchEngine Img(SearchesConfig config, BotPolicy policy)
        => policy.Managed ? MANAGED_IMG : config.ImgSearchEngine;

    // A managed bot uses the music service only when the manager set one (data/.music-service), never because
    // of searches.yml, which the bot owner can change through a restore.
    public static YoutubeSearcher Yt(SearchesConfig config, BotPolicy policy, MusicServiceSetting? service = null)
        => policy.Managed
            ? (service ?? MusicServiceSetting.Default).Current is null ? MANAGED_YT : YoutubeSearcher.Service
            : config.YtProvider;
}
