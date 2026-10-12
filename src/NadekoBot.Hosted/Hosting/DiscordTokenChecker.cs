using System.Net;
using System.Net.Http.Headers;

namespace NadekoBot.Hosted;

public enum TokenCheck
{
    Valid,
    Invalid,
    Unknown
}

public sealed class DiscordTokenChecker(HttpClient http)
{
    private const string ME_URL = "https://discord.com/api/v10/users/@me";
    private const string BOT_SCHEME = "Bot";

    public async Task<TokenCheck> CheckAsync(string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, ME_URL);
        req.Headers.Authorization = new AuthenticationHeaderValue(BOT_SCHEME, token);

        try
        {
            using var res = await http.SendAsync(req, ct);
            if (res.StatusCode == HttpStatusCode.Unauthorized)
                return TokenCheck.Invalid;

            return res.IsSuccessStatusCode ? TokenCheck.Valid : TokenCheck.Unknown;
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            Log.Warning(ex, "Could not reach Discord to check the token. Starting the bot anyway");
            return TokenCheck.Unknown;
        }
    }
}
