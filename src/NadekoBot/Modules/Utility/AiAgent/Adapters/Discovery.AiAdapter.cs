using System.Text;
using System.Text.Json;
using NadekoBot.AiAgent;

namespace NadekoBot.Modules.Utility.AiAgent.Adapters;

public sealed class DiscoveryAiAdapter(
    CommandSearchService commandSearch,
    DataToolSearchService dataToolSearch,
    Lazy<IAiToolRegistry> toolRegistry) : IAiCoreToolGroup, INService
{
    public string GroupName => "discovery";
    public string GroupDescription => "Search bot commands and data tools at runtime.";

    [AiTool(
        "search_commands",
        "Search for bot commands by describing what you want to do. "
        + "Returns matching commands with the first forms of their syntax, the parameters, an example, the options and the requirements.")]
    [AiSystemGuidance(SystemGuidanceText.SearchCommands)]
    public Task<string> SearchCommands(
        [AiParam("Describe what you want to do, e.g. 'mute a user temporarily' or 'play music from youtube'")]
        string query,
        [AiParam("Number of results to return (default 5, max 10)")]
        int count = 5)
    {
        if (!commandSearch.IsReady)
            return Task.FromResult("Error: Command search index is not ready yet. Try again in a moment.");

        if (string.IsNullOrWhiteSpace(query))
            throw ToolException.InvalidArgument("query is required.");

        count = Math.Clamp(count, 1, 10);

        var results = commandSearch.Search(query, count);
        if (results.Length == 0)
            return Task.FromResult("No matching commands found.");

        var sb = new StringBuilder();
        for (var i = 0; i < results.Length; i++)
            AgentHitFormatter.AppendCommand(sb, i + 1, results[i].Command);

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    [AiTool(
        "search_data_tools",
        "Search for data tools by describing what information you need. "
        + "Returns matching tools with their descriptions and arguments. Run one with invoke_data_tool.")]
    [AiSystemGuidance(SystemGuidanceText.SearchDataTools)]
    public Task<string> SearchDataTools(
        [AiParam("Describe what information you need, e.g. 'user xp level and rank' or 'server warning list'")]
        string query,
        [AiParam("Number of results to return (default 5, max 10)")]
        int count = 5)
    {
        if (!dataToolSearch.IsReady)
            return Task.FromResult("Error: Data tool search index is not ready yet. Try again in a moment.");

        if (string.IsNullOrWhiteSpace(query))
            throw ToolException.InvalidArgument("query is required.");

        count = Math.Clamp(count, 1, 10);

        var results = dataToolSearch.Search(query, count);
        if (results.Length == 0)
            return Task.FromResult("No matching data tools found.");

        var sb = new StringBuilder();
        for (var i = 0; i < results.Length; i++)
        {
            var entry = results[i].Entry;
            sb.Append(i + 1).Append(". ").Append(entry.Name).Append(": ").Append(entry.Description).Append('\n');

            if (toolRegistry.Value.GetTool(entry.Name) is { } tool)
                AgentHitFormatter.AppendToolParams(sb, tool.ParameterSchema);
        }

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    [AiTool(
        "invoke_data_tool",
        "Invoke a data tool by name with the given arguments. "
        + "Use search_data_tools to find the tool and its arguments first.")]
    public async Task<string> InvokeDataTool(
        AiToolContext ctx,
        [AiParam("The exact name of the data tool to invoke, e.g. 'get_user_xp'")]
        string name,
        [AiParam("A JSON object with the argument names which search_data_tools lists")]
        JsonElement args = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw ToolException.InvalidArgument("name is required.");

        var tool = toolRegistry.Value.GetTool(name);
        if (tool is null || !tool.IsDataTool)
            throw ToolException.NotFound($"Data tool '{name}' not found. Use search_data_tools to find it.");

        try
        {
            return await tool.ExecuteAsync(ctx, args);
        }
        catch (ToolException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error invoking data tool {ToolName}", name);
            // Same shape as a generated wrapper, so the LLM sees one error contract.
            throw ToolException.Internal(ex.Message);
        }
    }
}
