using System.Text.Json;

namespace NadekoBot.Modules.Utility.AiAgent.Tools;

public sealed class InvokeDataToolTool(Lazy<IAiToolRegistry> toolRegistry) : IAiTool, INService
{
    public string Name => "invoke_data_tool";

    public string Description =>
        "Invoke a data tool by name with the given arguments. " +
        "Use search_data_tools to find the tool, describe_data_tool to see its parameters, then this to run it.";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "name": {
                    "type": "string",
                    "description": "The exact name of the data tool to invoke, e.g. 'get_user_xp'"
                },
                "args": {
                    "type": "object",
                    "description": "Arguments to pass to the tool, matching its parameter schema"
                }
            },
            "required": ["name"]
        }
        """).RootElement.Clone();

    public async Task<string> ExecuteAsync(AiToolContext context, JsonElement arguments)
    {
        if (!arguments.TryGetProperty("name", out var nameEl)
            || string.IsNullOrWhiteSpace(nameEl.GetString()))
            return "Error: name is required.";

        var name = nameEl.GetString()!;
        var tool = toolRegistry.Value.GetTool(name);

        if (tool is null || !tool.IsDataTool)
            return $"Error: Data tool '{name}' not found.";

        var args = arguments.TryGetProperty("args", out var argsEl)
            ? argsEl
            : default;

        try
        {
            return await tool.ExecuteAsync(context, args);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error invoking data tool {ToolName}", name);
            return $"Error: {ex.Message}";
        }
    }
}
