using System.Globalization;
using System.Text.Json;

namespace NadekoBot.Db.ServerData;

public sealed record ServerExportManifest(
    ulong GuildId,
    string GuildName,
    DateTime CreatedAt,
    string BotVersion,
    string Schema,
    bool XpIncluded)
{
    public const string FORMAT = "nadeko-server-export";
    public const int VERSION = 1;
    public const int MAX_BYTES = 4096;
    public const int MAX_NAME_LENGTH = 100;
    public const int MAX_TEXT_LENGTH = 200;

    private const string FORMAT_PROP = "format";
    private const string VERSION_PROP = "version";
    private const string GUILD_ID_PROP = "guildId";
    private const string GUILD_NAME_PROP = "guildName";
    private const string CREATED_AT_PROP = "createdAt";
    private const string BOT_VERSION_PROP = "botVersion";
    private const string SCHEMA_PROP = "schema";
    private const string XP_INCLUDED_PROP = "xpIncluded";

    public void WriteTo(Stream stream)
    {
        using var writer = new Utf8JsonWriter(stream, new() { Indented = true });
        writer.WriteStartObject();
        writer.WriteString(FORMAT_PROP, FORMAT);
        writer.WriteNumber(VERSION_PROP, VERSION);
        writer.WriteString(GUILD_ID_PROP, GuildId.ToString(CultureInfo.InvariantCulture));
        writer.WriteString(GUILD_NAME_PROP, TruncateInternal(GuildName));
        writer.WriteString(CREATED_AT_PROP, CreatedAt);
        writer.WriteString(BOT_VERSION_PROP, BotVersion);
        writer.WriteString(SCHEMA_PROP, Schema);
        writer.WriteBoolean(XP_INCLUDED_PROP, XpIncluded);
        writer.WriteEndObject();
    }

    // Null when the json is not a manifest of a version which this bot reads.
    public static ServerExportManifest? TryParse(ReadOnlySpan<byte> json)
    {
        if (json.Length > MAX_BYTES)
            return null;

        try
        {
            var reader = new Utf8JsonReader(json, new() { MaxDepth = 2 });
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            if (!TryGetString(root, FORMAT_PROP, out var format) || format != FORMAT)
                return null;

            if (!root.TryGetProperty(VERSION_PROP, out var version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var v)
                || v != VERSION)
                return null;

            if (!TryGetString(root, GUILD_ID_PROP, out var guildIdText)
                || !ulong.TryParse(guildIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var guildId)
                || guildId == 0)
                return null;

            if (!TryGetString(root, SCHEMA_PROP, out var schema) || schema.Length == 0)
                return null;

            if (!root.TryGetProperty(CREATED_AT_PROP, out var createdAtEl)
                || createdAtEl.ValueKind != JsonValueKind.String
                || !createdAtEl.TryGetDateTime(out var createdAt))
                return null;

            if (!root.TryGetProperty(XP_INCLUDED_PROP, out var xpEl)
                || xpEl.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return null;

            var guildName = TryGetString(root, GUILD_NAME_PROP, out var name) ? name : string.Empty;
            var botVersion = TryGetString(root, BOT_VERSION_PROP, out var bv) ? bv : string.Empty;

            return new(guildId, guildName, createdAt.ToUniversalTime(), botVersion, schema, xpEl.GetBoolean());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ReadOnlySpan<char> TruncateInternal(string name)
    {
        if (name.Length <= MAX_NAME_LENGTH)
            return name;

        // Do not cut an emoji in half: a lone surrogate is not valid json text.
        var length = char.IsHighSurrogate(name[MAX_NAME_LENGTH - 1]) ? MAX_NAME_LENGTH - 1 : MAX_NAME_LENGTH;
        return name.AsSpan(0, length);
    }

    private static bool TryGetString(JsonElement root, string prop, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(prop, out var el) || el.ValueKind != JsonValueKind.String)
            return false;

        value = el.GetString() ?? string.Empty;
        return value.Length <= MAX_TEXT_LENGTH;
    }
}
