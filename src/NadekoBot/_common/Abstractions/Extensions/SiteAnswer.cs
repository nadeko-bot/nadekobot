namespace Nadeko.Common;

// One fixed message for each answer of an outside site which the bot cannot read, so Seq finds all of them by this template.
public static class SiteAnswer
{
    private const string TEMPLATE = "{Site} sent an answer which the bot cannot read at {JsonPath}: {Reason}";

    // Newtonsoft puts the value from the site into the message.
    private const int MAX_REASON_LENGTH = 256;

    // Other exceptions, for example of a JsonElement or JToken lookup, have no path.
    public static void LogUnreadable(string site, Exception ex)
    {
        var path = ex switch
        {
            System.Text.Json.JsonException j => j.Path,
            Newtonsoft.Json.JsonReaderException r => FromNewtonsoftPathInternal(r.Path),
            Newtonsoft.Json.JsonSerializationException s => FromNewtonsoftPathInternal(s.Path),
            _ => null
        };

        Log.Warning(TEMPLATE, site, path ?? "$", ReasonInternal(ex.Message));
    }

    private static string FromNewtonsoftPathInternal(string? path)
        => string.IsNullOrEmpty(path) ? "$"
            : path[0] == '[' ? "$" + path
            : "$." + path;

    // Both libraries repeat the path and the position at the end of the message.
    private static string ReasonInternal(string message)
    {
        var end = message.IndexOf(" Path: ", StringComparison.Ordinal);
        if (end < 0)
            end = message.IndexOf(" Path '", StringComparison.Ordinal);

        var length = Math.Min(end > 0 ? end : message.Length, MAX_REASON_LENGTH);
        return length == message.Length ? message : message[..length];
    }
}
