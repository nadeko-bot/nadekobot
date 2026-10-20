using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace NadekoBot.Common;

// Without Message Content, Discord sends a server message's text only when it mentions the bot: "@bot help" runs .help.
public static class MentionPrefix
{
    public static bool TryStrip(string content, ulong botId, string prefix, [NotNullWhen(true)] out string? command)
    {
        command = null;
        if (!TryTrimMention(content.AsSpan().TrimStart(), botId, out var rest))
            return false;

        rest = rest.TrimStart();
        if (rest.IsEmpty)
            return false;

        command = rest.StartsWith(prefix, StringComparison.InvariantCulture)
            ? rest.ToString()
            : string.Concat(prefix, rest);
        return true;
    }

    public static bool TryGetText(string content, ulong botId, [NotNullWhen(true)] out string? text)
    {
        text = null;
        if (!TryTrimMention(content.AsSpan().TrimStart(), botId, out var rest))
            return false;

        rest = rest.Trim();
        if (rest.IsEmpty)
            return false;

        text = rest.ToString();
        return true;
    }

    // Without Message Content, a reply which pings the bot has its text; other messages of the member are empty.
    public static string? ReadPromptInput(string content, ulong botId, bool hasMessageContent)
    {
        if (hasMessageContent)
            return content;

        if (TryGetText(content, botId, out var text))
            return text;

        return string.IsNullOrWhiteSpace(content) ? null : content;
    }

    // Discord writes a user mention as <@id>, and older clients as <@!id>.
    public static bool TryTrimMention(ReadOnlySpan<char> content, ulong botId, out ReadOnlySpan<char> rest)
    {
        rest = default;
        if (!content.StartsWith("<@", StringComparison.Ordinal))
            return false;

        var start = content.Length > 2 && content[2] == '!' ? 3 : 2;
        var end = content[start..].IndexOf('>');
        if (end <= 0
            || !ulong.TryParse(content.Slice(start, end), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || id != botId)
            return false;

        rest = content[(start + end + 1)..];
        return true;
    }
}
