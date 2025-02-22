#nullable disable
using StackExchange.Redis;
using System.Text.Json;
using System.Web;

namespace NadekoBot.Services;

/// <summary>
///     Uses <see cref="IStringsSource" /> to load strings into redis hash (only on Shard 0)
///     and retrieves them from redis via <see cref="GetText" />
/// </summary>
public class RedisBotStringsProvider : IBotStringsProvider
{
    private const string COMMANDS_KEY = "commands_v5";

    private readonly ConnectionMultiplexer _redis;
    private readonly IStringsSource _source;
    private readonly IBotCreds _creds;

    public RedisBotStringsProvider(
        ConnectionMultiplexer redis,
        DiscordSocketClient discordClient,
        IStringsSource source,
        IBotCreds creds)
    {
        _redis = redis;
        _source = source;
        _creds = creds;

        if (discordClient.ShardId == 0)
            Reload();
    }

    public string GetText(string localeName, string key)
    {
        var value = _redis.GetDatabase().HashGet($"{_creds.RedisKey()}:responses:{localeName}", key);
        return value;
    }

    public CommandStrings GetCommandStrings(string localeName, string commandName)
    {
        string examplesStr = _redis.GetDatabase()
                                   .HashGet($"{_creds.RedisKey()}:{COMMANDS_KEY}:{localeName}",
                                       $"{commandName}::examples");
        if (examplesStr == default)
            return null;

        var descStr = _redis.GetDatabase()
                            .HashGet($"{_creds.RedisKey()}:{COMMANDS_KEY}:{localeName}", $"{commandName}::desc");
        if (descStr == default)
            return null;

        var ex = examplesStr.Split('&').Map(HttpUtility.UrlDecode);

        var paramsStr = _redis.GetDatabase()
                              .HashGet($"{_creds.RedisKey()}:{COMMANDS_KEY}:{localeName}", $"{commandName}::params");
        if (paramsStr == default)
            return null;

        return new()
        {
            Examples = ex,
            Params = JsonSerializer.Deserialize<Dictionary<string, CommandStringParam>[]>(paramsStr),
            Desc = descStr
        };
    }

    public void Reload()
    {
        var redisDb = _redis.GetDatabase();
        foreach (var (localeName, localeStrings) in _source.GetResponseStrings())
        {
            var hashFields = localeStrings.Select(x => new HashEntry(x.Key, x.Value)).ToArray();

            redisDb.HashSet($"{_creds.RedisKey()}:responses:{localeName}", hashFields);
        }

        foreach (var (localeName, localeStrings) in _source.GetCommandStrings())
        {
            var hashFields = localeStrings
                             .Select(x => new HashEntry($"{x.Key}::examples",
                                 string.Join('&', x.Value.Examples.Map(HttpUtility.UrlEncode))))
                             .Concat(localeStrings.Select(x => new HashEntry($"{x.Key}::desc", x.Value.Desc)))
                             .Concat(localeStrings.Select(x
                                 => new HashEntry($"{x.Key}::params", JsonSerializer.Serialize(x.Value.Params))))
                             .ToArray();

            redisDb.HashSet($"{_creds.RedisKey()}:{COMMANDS_KEY}:{localeName}", hashFields);
        }
    }
}