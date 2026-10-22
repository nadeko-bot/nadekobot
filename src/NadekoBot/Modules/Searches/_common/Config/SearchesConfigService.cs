using NadekoBot.Common.Configs;
using NadekoBot.Modules.Music;

namespace NadekoBot.Modules.Searches;

public class SearchesConfigService : ConfigServiceBase<SearchesConfig>
{
    private static string FILE_PATH = "data/searches.yml";
    private static readonly TypedKey<SearchesConfig> _changeKey = new("config.searches.updated");

    public override string Name
        => "searches";

    public const int MAX_PER_SERVER = 1000;
    public const int MAX_MUSIC_SERVICE_URL_LENGTH = 200;

    public SearchesConfigService(IConfigSeria serializer, IPubSub pubSub, BotPolicy policy)
        : base(FILE_PATH, serializer, pubSub, _changeKey, policy)
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

        AddTextProp("musicServiceUrl",
            static sc => sc.MusicServiceUrl,
            static (sc, v) => sc.MusicServiceUrl = v,
            MAX_MUSIC_SERVICE_URL_LENGTH,
            "Url of the NadekoBot music service which the 'service' ytProvider uses",
            static v => v.Length == 0 || MusicServiceClient.ParseBaseUrl(v) is not null);

        AddNumberProp("followedStreams.maxCount",
            static sc => sc.FollowedStreams.MaxCount,
            static (sc, v) => sc.FollowedStreams.MaxCount = v,
            int.TryParse,
            ConfigPrinters.ToString,
            "Maximum number of streams that each server can follow. -1 for infinite",
            min: -1,
            max: MAX_PER_SERVER);

        AddNumberProp("feeds.maxCount",
            static sc => sc.MaxFeeds,
            static (sc, v) => sc.MaxFeeds = v,
            int.TryParse,
            ConfigPrinters.ToString,
            "Maximum number of feeds per server",
            min: 0,
            max: MAX_PER_SERVER);

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

        if (Data.Version < 7)
        {
            ModifyConfig(c =>
            {
                c.Version = 7;
            });
        }
    }
}