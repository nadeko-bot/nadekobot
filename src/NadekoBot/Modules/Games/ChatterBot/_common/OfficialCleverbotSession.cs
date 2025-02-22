#nullable disable
using Newtonsoft.Json;
using OneOf;
using OneOf.Types;

namespace NadekoBot.Modules.Games.Common.ChatterBot;

public class OfficialCleverbotSession : IChatterBotSession
{
    private string QueryString
        => $"https://www.cleverbot.com/getreply?key={_apiKey}" + "&wrapper=nadekobot" + "&input={0}" + "&cs={1}";

    private readonly string _apiKey;
    private readonly IHttpClientFactory _httpFactory;
    private string cs;

    public OfficialCleverbotSession(string apiKey, IHttpClientFactory factory)
    {
        _apiKey = apiKey;
        _httpFactory = factory;
    }

    public async Task<OneOf<ThinkResult, Error<string>>> Think(string input, string username)
    {
        using var http = _httpFactory.CreateClient();
        var dataString = await http.GetStringAsync(string.Format(QueryString, input, cs ?? ""));
        try
        {
            var data = JsonConvert.DeserializeObject<CleverbotResponse>(dataString);

            cs = data?.Cs;
            return new ThinkResult
            {
                Text = data?.Output,
                TokensIn = 2,
                TokensOut = 1
            };
        }
        catch
        {
            Log.Warning("Unexpected response from CleverBot: {ResponseString}", dataString);
            return new Error<string>("Unexpected CleverBot response received");
        }
    }
}