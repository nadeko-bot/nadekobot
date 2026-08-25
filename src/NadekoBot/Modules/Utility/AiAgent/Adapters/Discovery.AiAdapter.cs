using System.Text;
using System.Text.Json;
using NadekoBot.AiAgent;

namespace NadekoBot.Modules.Utility.AiAgent.Adapters;

// Discovery and dispatch for the command index and the data tool index.
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
        + "Returns matching commands with their syntax, description, examples, and required permissions. "
        + "Use this before run_command to find the right command.")]
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

    [AiTool(
        "search_data_tools",
        "Search for data tools by describing what information you need. "
        + "Returns matching tools with their names and descriptions. "
        + "Use describe_data_tool to get parameter details, then invoke_data_tool to run it.")]
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
            var (entry, score) = results[i];
            sb.AppendLine($"{i + 1}. {entry.Name} - {entry.Description} (score: {score:F3})");
        }

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    [AiTool(
        "describe_data_tool",
        "Get the full parameter schema for a data tool. "
        + "Use after search_data_tools to see what arguments a tool accepts before calling invoke_data_tool.")]
    public Task<string> DescribeDataTool(
        [AiParam("The exact name of the data tool, e.g. 'get_user_xp'")]
        string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw ToolException.InvalidArgument("name is required.");

        var tool = toolRegistry.Value.GetTool(name);
        if (tool is null || !tool.IsDataTool)
            throw ToolException.NotFound($"Data tool '{name}' not found.");

        var schema = new
        {
            name = tool.Name,
            description = tool.Description,
            parameters = tool.ParameterSchema
        };

        return Task.FromResult(JsonSerializer.Serialize(schema));
    }

    [AiTool(
        "invoke_data_tool",
        "Invoke a data tool by name with the given arguments. "
        + "Use search_data_tools to find the tool, describe_data_tool to see its parameters, then this to run it.")]
    public async Task<string> InvokeDataTool(
        AiToolContext ctx,
        [AiParam("The exact name of the data tool to invoke, e.g. 'get_user_xp'")]
        string name,
        [AiParam("Arguments to pass to the tool, matching its parameter schema")]
        JsonElement args = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw ToolException.InvalidArgument("name is required.");

        var tool = toolRegistry.Value.GetTool(name);
        if (tool is null || !tool.IsDataTool)
            throw ToolException.NotFound($"Data tool '{name}' not found.");

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
