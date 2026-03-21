using System.Text;
using System.Text.Json;

namespace NadekoBot.Modules.Utility.AiAgent.Tools;

/// <summary>
/// Searches bot commands by semantic similarity. Returns matching commands with
/// descriptions, examples, and usage - everything needed to call run_command.
/// </summary>
public sealed class SearchCommandsTool(CommandSearchService searchService) : IAiTool, INService
{
    public string Name => "search_commands";

    public string Description =>
        "Search for bot commands by describing what you want to do. " +
        "Returns matching commands with their syntax, description, examples, and required permissions. " +
        "Use this before run_command to find the right command.";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "query": {
                    "type": "string",
                    "description": "Describe what you want to do, e.g. 'mute a user temporarily' or 'play music from youtube'"
                },
                "count": {
                    "type": "integer",
                    "description": "Number of results to return (default 5, max 10)"
                }
            },
            "required": ["query"]
        }
        """).RootElement.Clone();

    public Task<string> ExecuteAsync(AiToolContext context, JsonElement arguments)
    {
        if (!searchService.IsReady)
            return Task.FromResult("Error: Command search index is not ready yet. Try again in a moment.");

        if (!arguments.TryGetProperty("query", out var queryEl)
            || string.IsNullOrWhiteSpace(queryEl.GetString()))
            return Task.FromResult("Error: query is required.");

        var query = queryEl.GetString()!;
        var count = 5;
        if (arguments.TryGetProperty("count", out var countEl) && countEl.TryGetInt32(out var c))
            count = Math.Clamp(c, 1, 10);

        var results = searchService.Search(query, count);
        if (results.Length == 0)
            return Task.FromResult("No matching commands found.");

        var sb = new StringBuilder();
        for (var i = 0; i < results.Length; i++)
        {
            var r = results[i];
            var cmd = r.Command;

            sb.AppendLine($"{i + 1}. {cmd.Aliases[0]} (aliases: {string.Join(", ", cmd.Aliases)})");
            sb.AppendLine($"   Module: {cmd.Module} > {cmd.Submodule}");
            sb.AppendLine($"   Description: {cmd.Description}");

            if (cmd.Usage.Length > 0)
                sb.AppendLine($"   Examples: {string.Join(", ", cmd.Usage)}");

            if (cmd.Requirements.Length > 0)
                sb.AppendLine($"   Requires: {string.Join(", ", cmd.Requirements)}");

            sb.AppendLine($"   Score: {r.Score:F3}");
            sb.AppendLine();
        }

        return Task.FromResult(sb.ToString().TrimEnd());
    }
}
