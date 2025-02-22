#nullable disable
namespace NadekoBot;

public interface IBotCreds
{
    string Token { get; }
    string NadekoAiToken { get; }
    ICollection<ulong> OwnerIds { get; set; }
    string GoogleApiKey { get; }
    bool UsePrivilegedIntents { get; }
    string RapidApiKey { get; }

    Creds.DbOptions Db { get; }
    string OsuApiKey { get; }
    int TotalShards { get; }
    Creds.PatreonSettings Patreon { get; }
    string CleverbotApiKey { get; }
    string Gpt3ApiKey { get; }
    RestartConfig RestartCommand { get; }
    Creds.VotesSettings Votes { get; }
    string BotListToken { get; }
    string RedisOptions { get; }
    string LocationIqApiKey { get; }
    string TimezoneDbApiKey { get; }
    string CoinmarketcapApiKey { get; }
    string TrovoClientId { get; }
    string CoordinatorUrl { get; set; }
    string TwitchClientId { get; set; }
    string TwitchClientSecret { get; set; }
    GoogleApiConfig Google { get; set; }
    BotCacheImplemenation BotCache { get; set; }
    Creds.GrpcApiConfig GrpcApi { get; set; }
    SeqConfig Seq { get; set; }
}

public interface IVotesSettings
{
    string TopggServiceUrl { get; set; }
    string TopggKey { get; set; }
    string DiscordsServiceUrl { get; set; }
    string DiscordsKey { get; set; }
}

public interface IPatreonSettings
{
    public string ClientId { get; set; }
    public string AccessToken { get; set; }
    public string RefreshToken { get; set; }
    public string ClientSecret { get; set; }
    public string CampaignId { get; set; }
}

public interface IRestartConfig
{
    string Cmd { get; set; }
    string Args { get; set; }
}

public class RestartConfig : IRestartConfig
{
    public string Cmd { get; set; }
    public string Args { get; set; }
}

public enum BotCacheImplemenation
{
    Memory,
    Redis
}

public interface IDbOptions
{
    string Type { get; set; }
    string ConnectionString { get; set; }
}

public interface IGoogleApiConfig
{
    string SearchId { get; init; }
    string ImageSearchId { get; init; }
}