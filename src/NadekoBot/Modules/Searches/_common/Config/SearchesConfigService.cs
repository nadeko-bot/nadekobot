using NadekoBot.Common.Configs;

namespace NadekoBot.Modules.Searches;

public class SearchesConfigService : ConfigServiceBase<SearchesConfig>
{
    private static string FILE_PATH = "data/searches.yml";
    private static readonly TypedKey<SearchesConfig> _changeKey = new("config.searches.updated");

    public override string Name
        => "searches";

    public SearchesConfigService(IConfigSeria serializer, IPubSub pubSub)
        : base(FILE_PATH, serializer, pubSub, _changeKey)
    {
        AddParsedProp("webEngine",
            static sc => sc.WebSearchEngine,
            static (sc, v) => sc.WebSearchEngine = v,
            ConfigParsers.InsensitiveEnum,
            ConfigPrinters.ToString,
            "Which engine should .search command use");

        AddParsedProp("imgEngine",
            static sc => sc.ImgSearchEngine,
            static (sc, v) => sc.ImgSearchEngine = v,
            ConfigParsers.InsensitiveEnum,
            ConfigPrinters.ToString,
            "Which engine should .image command use");

        AddParsedProp("ytProvider",
            static sc => sc.YtProvider,
            static (sc, v) => sc.YtProvider = v,
            ConfigParsers.InsensitiveEnum,
            ConfigPrinters.ToString,
            "Which search provider will be used for the .youtube and .q commands");

        AddParsedProp("followedStreams.maxCount",
            static sc => sc.FollowedStreams.MaxCount,
            static (sc, v) => sc.FollowedStreams.MaxCount = v,
            int.TryParse,
            ConfigPrinters.ToString,
            "Maximum number of streams that each server can follow. -1 for infinite");

        AddParsedProp("feeds.maxCount",
            static sc => sc.MaxFeeds,
            static (sc, v) => sc.MaxFeeds = v,
            int.TryParse,
            ConfigPrinters.ToString,
            "Maximum number of feeds per server");

        Migrate();
    }

    private void Migrate()
    {
        if (Data.Version < 1)
        {
            ModifyConfig(c =>
            {
                c.Version = 1;
                c.WebSearchEngine = WebSearchEngine.Google_Scrape;
            });
        }

        if (Data.Version < 6)
        {
            ModifyConfig(c =>
            {
                c.Version = 6;
            });
        }
    }
}