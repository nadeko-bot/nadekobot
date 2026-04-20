using System.Text;
using System.Text.Json;

namespace NadekoBot.Modules.Utility.AiAgent.Tools;

public sealed class SearchDataToolsTool(DataToolSearchService searchService) : IAiTool, INService
{
    public string Name => "search_data_tools";

    public string Description =>
        "Search for data tools by describing what information you need. " +
        "Returns matching tools with their names and descriptions. " +
        "Use describe_data_tool to get parameter details, then invoke_data_tool to run it.";

    public string? SystemGuidance => """
        DATA TOOLS:
        For reading information about this server (XP, warnings, waifus, reminders, configuration, etc.),
        use search_data_tools to find a matching data tool, optionally describe_data_tool for its parameters,
        then invoke_data_tool to get structured data. Do NOT run a bot command and read its embed to get data.
        Data tools never mutate state.
        """;

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "query": {
                    "type": "string",
                    "description": "Describe what information you need, e.g. 'user xp level and rank' or 'server warning list'"
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
            return Task.FromResult("Error: Data tool search index is not ready yet. Try again in a moment.");

        if (!arguments.TryGetProperty("query", out var queryEl)
            || string.IsNullOrWhiteSpace(queryEl.GetString()))
            return Task.FromResult("Error: query is required.");

        var query = queryEl.GetString()!;
        var count = 5;
        if (arguments.TryGetProperty("count", out var countEl) && countEl.TryGetInt32(out var c))
            count = Math.Clamp(c, 1, 10);

        var results = searchService.Search(query, count);
        if (results.Length == 0)
            return Task.FromResult("No matching data tools found.");

        var sb = new StringBuilder();
        for (var i = 0; i < results.Length; i++)
        {
            var (entry, score) = results[i];
            sb.AppendLine($"{i + 1}. {entry.Name} - {entry.Description} (score: {score:F3})");
        }

        return Task.FromResult(sb.ToString().TrimEnd());
    }
}
