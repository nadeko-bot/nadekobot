#nullable disable
using NadekoBot.Common.Yml;

namespace NadekoBot.Common;

public sealed class Creds : IBotCreds
{
    [Comment("""DO NOT CHANGE""")]
    public int Version { get; set; } = 13;

    [Comment("""Bot token. Do not share with anyone ever -> https://discordapp.com/developers/applications/""")]
    public string Token { get; set; }

    [Comment("""
             List of Ids of the users who have bot owner permissions
             **DO NOT ADD PEOPLE YOU DON'T TRUST**
             """)]
    public ICollection<ulong> OwnerIds { get; set; }

    [Comment(
        "Keep this on 'true' unless you're sure your bot shouldn't use privileged intents or you're waiting to be accepted")]
    public bool UsePrivilegedIntents { get; set; }

    [Comment("""
             The number of shards that the bot will be running on.
             Leave at 1 if you don't know what you're doing.

             note: If you are planning to have more than one shard, then you must change botCache to 'redis'.
                   Also, in that case you should be using NadekoBot.Coordinator to start the bot, and it will correctly override this value.
             """)]
    public int TotalShards { get; set; }

    [Comment("""
             Pledge 5$ or more on https://patreon.com/nadekobot and connect your discord account to Patreon.
             Go to https://dashy.nadeko.bot/me and login with your discord account
             Go to the Keys page and click "Generate New Key" and copy it here
             You and anyone else with the permission to run `.prompt` command will be able to use natural language to run bot's commands.
             For example '@Bot how's the weather in Paris' will return the current weather in Paris as if you were to run `.weather Paris` command.
             """)]
    public string NadekoAiToken { get; set; }

    [Comment(
        """
        Login to https://console.cloud.google.com, create a new project, go to APIs & Services -> Library -> YouTube Data API and enable it.
        Then, go to APIs and Services -> Credentials and click Create credentials -> API key.
        Used only for Youtube Data Api (at the moment).
        """)]
    public string GoogleApiKey { get; set; }

    [Comment(
        """
        Create a new custom search here https://programmablesearchengine.google.com/cse/create/new
        Enable SafeSearch
        Remove all Sites to Search
        Enable Search the entire web
        Copy the 'Search Engine ID' to the SearchId field

        Do all steps again but enable image search for the ImageSearchId
        """)]
    public GoogleApiConfig Google { get; set; }

    [Comment("""Settings for voting system for discordbots. Meant for use on global Nadeko.""")]
    public VotesSettings Votes { get; set; }

    [Comment("""
             Patreon auto reward system settings.
             go to https://www.patreon.com/portal -> my clients -> create client
             """)]
    public PatreonSettings Patreon { get; set; }

    [Comment("""Api key for sending stats to DiscordBotList.""")]
    public string BotListToken { get; set; }

    [Comment("""Official cleverbot api key.""")]
    public string CleverbotApiKey { get; set; }

    [Comment(@"OpenAi api key.")]
    public string Gpt3ApiKey { get; set; }

    [Comment("""
             Which cache implementation should bot use.
             'memory' - Cache will be in memory of the bot's process itself. Only use this on bots with a single shard. When the bot is restarted the cache is reset. 
             'redis' - Uses redis (which needs to be separately downloaded and installed). The cache will persist through bot restarts. You can configure connection string in creds.yml
             """)]
    public BotCacheImplemenation BotCache { get; set; }

    [Comment("""
             Redis connection string. Don't change if you don't know what you're doing.
             Only used if botCache is set to 'redis'
             """)]
    public string RedisOptions { get; set; }

    [Comment("""Database options. Don't change if you don't know what you're doing. Leave null for default values""")]
    public DbOptions Db { get; set; }

    [Comment("""
             Address and port of the coordinator endpoint. Leave empty for default.
             Change only if you've changed the coordinator address or port.
             """)]
    public string CoordinatorUrl { get; set; }

    [Comment(
        """Api key obtained on https://rapidapi.com (go to MyApps -> Add New App -> Enter Name -> Application key)""")]
    public string RapidApiKey { get; set; }

    [Comment("""
             https://locationiq.com api key (register and you will receive the token in the email).
             Used only for .time command.
             """)]
    public string LocationIqApiKey { get; set; }

    [Comment("""
             https://timezonedb.com api key (register and you will receive the token in the email).
             Used only for .time command
             """)]
    public string TimezoneDbApiKey { get; set; }

    [Comment("""
             https://pro.coinmarketcap.com/account/ api key. There is a free plan for personal use.
             Used for cryptocurrency related commands.
             """)]
    public string CoinmarketcapApiKey { get; set; }

//     [Comment(@"https://polygon.io/dashboard/api-keys api key. Free plan allows for 5 queries per minute.
// Used for stocks related commands.")]
//     public string PolygonIoApiKey { get; set; }

    [Comment("""Api key used for Osu related commands. Obtain this key at https://osu.ppy.sh/p/api""")]
    public string OsuApiKey { get; set; }

    [Comment("""
             Optional Trovo client id.
             You should use this if Trovo stream notifications stopped working or you're getting ratelimit errors.
             """)]
    public string TrovoClientId { get; set; }

    [Comment("""Obtain by creating an application at https://dev.twitch.tv/console/apps""")]
    public string TwitchClientId { get; set; }

    [Comment("""Obtain by creating an application at https://dev.twitch.tv/console/apps""")]
    public string TwitchClientSecret { get; set; }

    [Comment("""
             Command and args which will be used to restart the bot.
             Only used if bot is executed directly (NOT through the coordinator)
             placeholders: 
                 {0} -> shard id 
                 {1} -> total shards
             Linux default
                 cmd: dotnet
                 args: "NadekoBot.dll -- {0}"
             Windows default
                 cmd: NadekoBot.exe
                 args: "{0}"
             """)]
    public RestartConfig RestartCommand { get; set; }


    [Comment("""
             Settings for the grpc api. 
             We don't provide support for this.
             If you leave certPath empty, the api will run on http.
             """)]
    public GrpcApiConfig GrpcApi { get; set; }

    [Comment("""
             Url and api key to a seq server. If url is set, bot will try to send logs to it.
             """)]
    public SeqConfig Seq { get; set; }

    public Creds()
    {
        Token = string.Empty;
        UsePrivilegedIntents = true;
        OwnerIds = new List<ulong>();
        TotalShards = 1;
        GoogleApiKey = string.Empty;
        Votes = new VotesSettings(string.Empty, string.Empty, string.Empty, string.Empty);
        Patreon = new PatreonSettings(string.Empty, string.Empty, string.Empty, string.Empty);
        BotListToken = string.Empty;
        CleverbotApiKey = string.Empty;
        Gpt3ApiKey = string.Empty;
        BotCache = BotCacheImplemenation.Memory;
        RedisOptions = "localhost:6379,syncTimeout=30000,responseTimeout=30000,allowAdmin=true,password=";
        Db = new DbOptions()
        {
            Type = "sqlite",
            ConnectionString = "Data Source=data/NadekoBot.db"
        };

        CoordinatorUrl = "http://localhost:3442";

        RestartCommand = new RestartConfig();
        Google = new GoogleApiConfig();

        GrpcApi = new();
        Seq = new();
    }

    public class DbOptions
        : IDbOptions
    {
        [Comment("""
                 Database type. "sqlite", "mysql" and "postgresql" are supported.
                 Default is "sqlite"
                 """)]
        public string Type { get; set; }

        [Comment("""
                 Database connection string.
                 You MUST change this if you're not using "sqlite" type.
                 Default is "Data Source=data/NadekoBot.db"
                 Example for mysql: "Server=localhost;Port=3306;Uid=root;Pwd=my_super_secret_mysql_password;Database=nadeko"
                 Example for postgresql: "Server=localhost;Port=5432;User Id=postgres;Password=my_super_secret_postgres_password;Database=nadeko;"
                 """)]
        public string ConnectionString { get; set; }
    }

    public sealed record PatreonSettings : IPatreonSettings
    {
        public string ClientId { get; set; }
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public string ClientSecret { get; set; }

        [Comment(
            """Campaign ID of your patreon page. Go to your patreon page (make sure you're logged in) and type "prompt('Campaign ID', window.patreon.bootstrap.creator.data.id);" in the console. (ctrl + shift + i)""")]
        public string CampaignId { get; set; }

        public PatreonSettings(
            string accessToken,
            string refreshToken,
            string clientSecret,
            string campaignId)
        {
            AccessToken = accessToken;
            RefreshToken = refreshToken;
            ClientSecret = clientSecret;
            CampaignId = campaignId;
        }

        public PatreonSettings()
        {
        }
    }

    public sealed record VotesSettings : IVotesSettings
    {
        [Comment("""
                 top.gg votes service url
                 This is the url of your instance of the NadekoBot.Votes api
                 Example: https://votes.my.cool.bot.com
                 """)]
        public string TopggServiceUrl { get; set; }

        [Comment("""
                 Authorization header value sent to the TopGG service url with each request
                 This should be equivalent to the TopggKey in your NadekoBot.Votes api appsettings.json file
                 """)]
        public string TopggKey { get; set; }

        [Comment("""
                 discords.com votes service url
                 This is the url of your instance of the NadekoBot.Votes api
                 Example: https://votes.my.cool.bot.com
                 """)]
        public string DiscordsServiceUrl { get; set; }

        [Comment("""
                 Authorization header value sent to the Discords service url with each request
                 This should be equivalent to the DiscordsKey in your NadekoBot.Votes api appsettings.json file
                 """)]
        public string DiscordsKey { get; set; }

        public VotesSettings()
        {
        }

        public VotesSettings(
            string topggServiceUrl,
            string topggKey,
            string discordsServiceUrl,
            string discordsKey)
        {
            TopggServiceUrl = topggServiceUrl;
            TopggKey = topggKey;
            DiscordsServiceUrl = discordsServiceUrl;
            DiscordsKey = discordsKey;
        }
    }

    public sealed record GrpcApiConfig
    {
        public bool Enabled { get; set; } = false;
        public string CertChain { get; set; } = string.Empty;
        public string CertPrivateKey { get; set; } = string.Empty;
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 43120;
    }
}

public sealed class SeqConfig
{
    public string Url { get; init; }
    public string ApiKey { get; init; }
}

public class GoogleApiConfig : IGoogleApiConfig
{
    public string SearchId { get; init; }
    public string ImageSearchId { get; init; }
}