using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NadekoBot.Modules.Utility.AiAgent;

// Turns agent steps into short lines for members who do not know what a tool is.
public static partial class AgentActivityFormatter
{
    private const int MAX_ARG_LENGTH = 60;
    private const int MAX_NOTE_LENGTH = 200;
    private const int MAX_COMMAND_NAME_LENGTH = 32;
    private const int MAX_CHAIN_COMMANDS = 5;
    private const string ELLIPSIS = "...";
    private const string SUBTEXT = "-# ";

    private const string NOTE_EMOJI = "💭";
    private const string SEARCH_EMOJI = "🔍";
    private const string DATA_EMOJI = "📊";
    private const string RUN_EMOJI = "▶️";
    private const string MESSAGE_EMOJI = "💬";
    private const string WEB_EMOJI = "🌐";
    private const string TIME_EMOJI = "🕒";
    private const string OTHER_EMOJI = "⚙️";

    private static readonly string[] _verbPrefixes = ["get_", "list_", "find_", "count_", "describe_", "check_"];

    [GeneratedRegex(@"^<#(\d+)>$")]
    private static partial Regex ChannelMentionRegex();

    public static string FormatNote(string text)
        => NOTE_EMOJI + " " + OneLine(text, MAX_NOTE_LENGTH);

    // Null for a tool which only serves other tools, or which shows its own message.
    public static string? FormatToolCall(
        string name,
        JsonElement args,
        bool failed,
        string prefix,
        IBotStrings strings,
        ulong guildId)
    {
        if (!TryDescribeInternal(name, args, prefix, out var emoji, out var label))
            return null;

        var text = strings.GetText(label.Key, guildId, label.Params);
        if (failed)
        {
            var failedLabel = strs.agent_step_failed(text);
            text = strings.GetText(failedLabel.Key, guildId, failedLabel.Params);
        }

        return emoji + " " + text;
    }

    // Keeps the newest lines which fit, and counts the rest in one line at the top.
    public static string Render(
        IReadOnlyList<string> lines,
        int dropped,
        int maxLength,
        string? header,
        bool subtext,
        IBotStrings strings,
        ulong guildId)
    {
        var linePrefix = subtext ? SUBTEXT : string.Empty;
        var reserve = (header?.Length + 1 ?? 0)
                      + linePrefix.Length
                      + EarlierText(dropped + lines.Count, strings, guildId).Length
                      + 1;

        var budget = maxLength - reserve;
        var used = 0;
        var first = lines.Count;
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var cost = linePrefix.Length + lines[i].Length + 1;
            if (used + cost > budget)
                break;

            used += cost;
            first = i;
        }

        var hidden = dropped + first;
        var sb = new StringBuilder(maxLength);

        if (header is not null)
            sb.Append(header).Append('\n');

        if (hidden > 0)
            sb.Append(linePrefix).Append(EarlierText(hidden, strings, guildId)).Append('\n');

        for (var i = first; i < lines.Count; i++)
            sb.Append(linePrefix).Append(lines[i]).Append('\n');

        if (sb.Length > 0)
            sb.Length--;

        return sb.ToString();
    }

    private static string EarlierText(int count, IBotStrings strings, ulong guildId)
    {
        var loc = strs.agent_activity_earlier(count);
        return "*" + strings.GetText(loc.Key, guildId, loc.Params) + "*";
    }

    private static bool TryDescribeInternal(
        string name,
        JsonElement args,
        string prefix,
        out string emoji,
        out LocStr label)
    {
        emoji = OTHER_EMOJI;
        label = default;

        switch (name)
        {
            case "describe_data_tool":
            case "ask_user":
            case "close_session":
                return false;
            case "search_commands" when GetString(args, "query") is { } query:
                (emoji, label) = (SEARCH_EMOJI, strs.agent_step_search_commands(Arg(query)));
                return true;
            case "search_data_tools" when GetString(args, "query") is { } query:
                (emoji, label) = (SEARCH_EMOJI, strs.agent_step_search_data(Arg(query)));
                return true;
            case "invoke_data_tool" when GetString(args, "name") is { } tool:
                (emoji, label) = (DATA_EMOJI, strs.agent_step_data(Humanize(tool)));
                return true;
            case "run_command" when CommandName(GetString(args, "command"), prefix) is { } command:
                (emoji, label) = (RUN_EMOJI, strs.agent_step_run_command(command));
                return true;
            case "run_command_chain" when CommandNames(args, prefix) is { } commands:
                (emoji, label) = (RUN_EMOJI, strs.agent_step_run_command(commands));
                return true;
            case "send_message" when GetString(args, "channel") is { } channel:
                (emoji, label) = (MESSAGE_EMOJI, strs.agent_step_send_message(Channel(channel)));
                return true;
            case "edit_message":
                (emoji, label) = (MESSAGE_EMOJI, strs.agent_step_edit_message);
                return true;
            case "get_message":
                (emoji, label) = (MESSAGE_EMOJI, strs.agent_step_get_message);
                return true;
            case "delete_message":
                (emoji, label) = (MESSAGE_EMOJI, strs.agent_step_delete_message);
                return true;
            case "search_messages":
                (emoji, label) = (MESSAGE_EMOJI, strs.agent_step_search_messages);
                return true;
            case "web_search" when GetString(args, "query") is { } query:
                (emoji, label) = (WEB_EMOJI, strs.agent_step_web_search(Arg(query)));
                return true;
            case "compute_timestamp":
                (emoji, label) = (TIME_EMOJI, strs.agent_step_time);
                return true;
            default:
                label = strs.agent_step_other(Humanize(name));
                return true;
        }
    }

    private static string? GetString(JsonElement args, string property)
        => args.ValueKind == JsonValueKind.Object
           && args.TryGetProperty(property, out var value)
           && value.ValueKind == JsonValueKind.String
           && value.GetString() is { } text
           && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;

    private static string Arg(string text)
        => Format.Sanitize(OneLine(text, MAX_ARG_LENGTH));

    // "get_user_xp" becomes "user xp".
    private static string Humanize(string toolName)
    {
        var span = toolName.AsSpan();
        foreach (var verb in _verbPrefixes)
        {
            if (span.StartsWith(verb, StringComparison.Ordinal) && span.Length > verb.Length)
            {
                span = span[verb.Length..];
                break;
            }
        }

        return Arg(span.ToString().Replace('_', ' '));
    }

    // Only the name of the command, because its arguments can hold mentions and text of other members.
    private static string? CommandName(string? command, string prefix)
    {
        if (command is null)
            return null;

        var span = command.AsSpan().Trim();
        if (prefix.Length > 0 && span.StartsWith(prefix, StringComparison.Ordinal))
            span = span[prefix.Length..];

        for (var i = 0; i < span.Length; i++)
        {
            if (char.IsWhiteSpace(span[i]))
            {
                span = span[..i];
                break;
            }
        }

        if (span.IsEmpty || span.Contains('`') || prefix.Contains('`'))
            return null;

        if (span.Length > MAX_COMMAND_NAME_LENGTH)
            span = span[..MAX_COMMAND_NAME_LENGTH];

        return string.Concat("`", prefix, span, "`");
    }

    private static string? CommandNames(JsonElement args, string prefix)
    {
        if (args.ValueKind != JsonValueKind.Object
            || !args.TryGetProperty("commands", out var commands)
            || commands.ValueKind != JsonValueKind.Array)
            return null;

        var sb = new StringBuilder();
        var count = 0;
        foreach (var item in commands.EnumerateArray())
        {
            if (count == MAX_CHAIN_COMMANDS)
                break;

            if (item.ValueKind != JsonValueKind.String || CommandName(item.GetString(), prefix) is not { } name)
                continue;

            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append(name);
            count++;
        }

        return sb.Length == 0 ? null : sb.ToString();
    }

    private static string Channel(string input)
    {
        var trimmed = input.Trim();

        var match = ChannelMentionRegex().Match(trimmed);
        if (match.Success)
            return trimmed;

        if (ulong.TryParse(trimmed, out var id))
            return $"<#{id}>";

        return "#" + Arg(trimmed.TrimStart('#'));
    }

    // Collapses all whitespace, and cuts at a word boundary when the text is too long.
    private static string OneLine(string text, int maxLength)
    {
        var source = text.AsSpan().Trim();
        var sb = new StringBuilder(Math.Min(source.Length, maxLength + ELLIPSIS.Length));
        var pendingSpace = false;
        var truncated = false;

        foreach (var c in source)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = true;
                continue;
            }

            if (sb.Length + (pendingSpace ? 2 : 1) > maxLength)
            {
                truncated = true;
                break;
            }

            if (pendingSpace)
                sb.Append(' ');

            pendingSpace = false;
            sb.Append(c);
        }

        if (!truncated)
            return sb.ToString();

        for (var i = sb.Length - 1; i > maxLength / 2; i--)
        {
            if (sb[i] == ' ')
            {
                sb.Length = i;
                break;
            }
        }

        return sb.Append(ELLIPSIS).ToString();
    }
}
