namespace NadekoBot.Modules.Searches;

// A managed bot has no API keys or self-hosted instances, so it always uses the engines which need neither.
public static class SearchEngineChoice
{
    public const WebSearchEngine MANAGED_WEB = WebSearchEngine.Google_Scrape;
    public const ImgSearchEngine MANAGED_IMG = ImgSearchEngine.DuckDuckGo;
    public const YoutubeSearcher MANAGED_YT = YoutubeSearcher.Ytdlp;

    public static WebSearchEngine Web(SearchesConfig config, BotPolicy policy)
        => policy.Managed ? MANAGED_WEB : config.WebSearchEngine;

    public static ImgSearchEngine Img(SearchesConfig config, BotPolicy policy)
        => policy.Managed ? MANAGED_IMG : config.ImgSearchEngine;

    public static YoutubeSearcher Yt(SearchesConfig config, BotPolicy policy)
        => policy.Managed ? MANAGED_YT : config.YtProvider;
}
