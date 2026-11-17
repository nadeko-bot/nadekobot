using System.Text;
using System.Text.Json;

namespace NadekoBot.Modules.Utility.AiAgent;

public static class AgentHitFormatter
{
    // Commands like .config have many forms; the first ones are the common ones.
    private const int MAX_OVERLOADS = 4;
    private const int MAX_OPTIONS = 6;
    private const int MAX_EXAMPLES = 5;
    // No score floor: small talk and real requests overlap in score (output/agent-rewrite/candidate-scores.tsv).
    public const int CANDIDATES = 4;

    // Every request carries the candidates, so commands with long parameter texts must not fill the prompt.
    private const int MAX_CANDIDATE_CHARS = 6_000;

    public static void AppendCommand(StringBuilder sb, int number, CommandEntry cmd)
    {
        sb.Append(number).Append(". ").Append(cmd.Aliases[0]);
        if (cmd.Aliases.Length > 1)
        {
            sb.Append(" (also ");
            for (var i = 1; i < cmd.Aliases.Length; i++)
            {
                if (i > 1)
                    sb.Append(", ");
                sb.Append(cmd.Aliases[i]);
            }

            sb.Append(')');
        }

        sb.Append(": ");
        AppendOneLineInternal(sb, cmd.Description);
        sb.Append('\n');

        AppendUsage(sb, cmd);

        if (cmd.Options.Length > 0)
        {
            sb.Append("   Options: ");
            var count = Math.Min(cmd.Options.Length, MAX_OPTIONS);
            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                    sb.Append("; ");
                AppendOneLineInternal(sb, cmd.Options[i]);
            }

            sb.Append('\n');
        }

        if (cmd.Requirements.Length > 0)
            sb.Append("   Requires: ").AppendJoin(", ", cmd.Requirements).Append('\n');
    }

    // An example goes next to its form only when cmds.yml has one example for each form.
    public static void AppendUsage(StringBuilder sb, CommandEntry cmd)
    {
        var forms = cmd.Params.Length;
        var examples = cmd.Usage.Length;
        if (forms == 0 && examples == 0)
        {
            sb.Append("   Usage: ").Append(cmd.Aliases[0]).Append('\n');
            return;
        }

        var paired = forms == examples;
        var shown = Math.Min(forms, MAX_OVERLOADS);
        for (var i = 0; i < shown; i++)
        {
            var ps = cmd.Params[i];
            sb.Append("   Usage: ").Append(cmd.Aliases[0]);
            foreach (var p in ps)
                sb.Append(" <").Append(p.Name).Append('>');

            if (paired && !string.IsNullOrWhiteSpace(cmd.Usage[i]))
                sb.Append("  Example: ").Append(cmd.Usage[i]);

            sb.Append('\n');

            foreach (var p in ps)
            {
                if (string.IsNullOrWhiteSpace(p.Desc))
                    continue;

                sb.Append("     ").Append(p.Name).Append(": ");
                AppendOneLineInternal(sb, p.Desc);
                sb.Append('\n');
            }
        }

        var hidden = forms - shown;
        if (hidden > 0)
            sb.Append("   (").Append(hidden).Append(hidden == 1 ? " more form)\n" : " more forms)\n");

        if (paired)
            return;

        var count = Math.Min(examples, MAX_EXAMPLES);
        for (var i = 0; i < count; i++)
        {
            if (!string.IsNullOrWhiteSpace(cmd.Usage[i]))
                sb.Append("   Example: ").Append(cmd.Usage[i]).Append('\n');
        }
    }

    // The best matches for the member's own words, so a request which one command answers needs no search step.
    public static string? Candidates(CommandSearchService search, string request)
    {
        if (!search.IsReady || string.IsNullOrWhiteSpace(request))
            return null;

        // The request must still reach the model when the search fails.
        CommandSearchResult[] hits;
        try
        {
            hits = search.Search(request, CANDIDATES);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AI agent could not find the command candidates");
            return null;
        }

        if (hits.Length == 0)
            return null;

        var sb = new StringBuilder("\n\n[Commands which may fit this request, found by search_commands. ")
            .Append("Run one only when it does what the member asked:]\n");
        for (var i = 0; i < hits.Length; i++)
        {
            var before = sb.Length;
            AppendCommand(sb, i + 1, hits[i].Command);
            if (i > 0 && sb.Length > MAX_CANDIDATE_CHARS)
            {
                sb.Length = before;
                break;
            }
        }

        return sb.ToString().TrimEnd();
    }

    // "name (string, required): what it is", so the model can call the tool without a second lookup.
    public static void AppendToolParams(StringBuilder sb, JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object
            || !schema.TryGetProperty("properties", out var props)
            || props.ValueKind != JsonValueKind.Object)
        {
            sb.Append("   Args: none\n");
            return;
        }

        var required = new HashSet<string>(StringComparer.Ordinal);
        if (schema.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in req.EnumerateArray())
            {
                if (r.GetString() is { } s)
                    required.Add(s);
            }
        }

        var any = false;
        foreach (var prop in props.EnumerateObject())
        {
            any = true;
            sb.Append("   - ").Append(prop.Name).Append(" (");
            AppendTypeInternal(sb, prop.Value);
            if (required.Contains(prop.Name))
                sb.Append(", required");
            sb.Append(')');

            if (prop.Value.TryGetProperty("description", out var desc) && desc.ValueKind == JsonValueKind.String)
            {
                sb.Append(": ");
                AppendOneLineInternal(sb, desc.GetString() ?? "");
            }

            sb.Append('\n');
        }

        if (!any)
            sb.Append("   Args: none\n");
    }

    // "one of day|week", "array of integer": a small model guesses wrong values without them.
    private static void AppendTypeInternal(StringBuilder sb, JsonElement schema)
    {
        if (schema.TryGetProperty("enum", out var values) && values.ValueKind == JsonValueKind.Array)
        {
            sb.Append("one of ");
            var first = true;
            foreach (var v in values.EnumerateArray())
            {
                if (!first)
                    sb.Append('|');
                sb.Append(v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText());
                first = false;
            }

            return;
        }

        var type = schema.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString() ?? "any"
            : "any";
        sb.Append(type);

        if (type == "array" && schema.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object)
        {
            sb.Append(" of ");
            AppendTypeInternal(sb, items);
        }
    }

    private static void AppendOneLineInternal(StringBuilder sb, string text)
    {
        var span = text.AsSpan().Trim();
        var space = false;
        foreach (var c in span)
        {
            if (c is '\n' or '\r' or '\t')
            {
                if (!space)
                    sb.Append(' ');
                space = true;
                continue;
            }

            sb.Append(c);
            space = c == ' ';
        }
    }
}
