using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

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
    private const string APPLICATION_URL = "https://discord.com/api/v10/applications/@me";
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

    // Null when Discord did not say which privileged intents the application has.
    public async Task<IntentState?> ReadIntentsAsync(string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, APPLICATION_URL);
        req.Headers.Authorization = new AuthenticationHeaderValue(BOT_SCHEME, token);

        try
        {
            using var res = await http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
                return null;

            await using var body = await res.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(body, cancellationToken: ct);
            return ParseIntents(doc.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                       || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            Log.Warning(ex, "Could not read the privileged intents of the application from Discord");
            return null;
        }
    }

    public static IntentState? ParseIntents(JsonElement application)
        => application.ValueKind == JsonValueKind.Object
           && application.TryGetProperty("flags", out var flags)
           && flags.ValueKind == JsonValueKind.Number
           && flags.TryGetInt64(out var value)
            ? IntentState.FromApplicationFlags(value)
            : null;
}
