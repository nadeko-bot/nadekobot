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
            sc => sc.WebSearchEngine,
            ConfigParsers.InsensitiveEnum,
            ConfigPrinters.ToString);

        AddParsedProp("imgEngine",
            sc => sc.ImgSearchEngine,
            ConfigParsers.InsensitiveEnum,
            ConfigPrinters.ToString);

        AddParsedProp("ytProvider",
            sc => sc.YtProvider,
            ConfigParsers.InsensitiveEnum,
            ConfigPrinters.ToString);

        AddParsedProp("followedStreams.maxCount",
            sc => sc.FollowedStreams.MaxCount,
            int.TryParse,
            ConfigPrinters.ToString);

        Migrate();
    }

    private void Migrate()
    {
        if (data.Version < 1)
        {
            ModifyConfig(c =>
            {
                c.Version = 1;
                c.WebSearchEngine = WebSearchEngine.Google_Scrape;
            });
        }

        if (data.Version < 4)
        {
            ModifyConfig(c =>
            {
                c.Version = 4;
            });
        }
    }
}