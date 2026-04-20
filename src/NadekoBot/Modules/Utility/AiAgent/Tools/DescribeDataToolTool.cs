using System.Text.Json;

namespace NadekoBot.Modules.Utility.AiAgent.Tools;

public sealed class DescribeDataToolTool(Lazy<IAiToolRegistry> toolRegistry) : IAiTool, INService
{
    public string Name => "describe_data_tool";

    public string Description =>
        "Get the full parameter schema for a data tool. " +
        "Use after search_data_tools to see what arguments a tool accepts before calling invoke_data_tool.";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "name": {
                    "type": "string",
                    "description": "The exact name of the data tool, e.g. 'get_user_xp'"
                }
            },
            "required": ["name"]
        }
        """).RootElement.Clone();

    public Task<string> ExecuteAsync(AiToolContext context, JsonElement arguments)
    {
        if (!arguments.TryGetProperty("name", out var nameEl)
            || string.IsNullOrWhiteSpace(nameEl.GetString()))
            return Task.FromResult("Error: name is required.");

        var name = nameEl.GetString()!;
        var tool = toolRegistry.Value.GetTool(name);

        if (tool is null || !tool.IsDataTool)
            return Task.FromResult($"Error: Data tool '{name}' not found.");

        var schema = new
        {
            name = tool.Name,
            description = tool.Description,
            parameters = tool.ParameterSchema
        };

        return Task.FromResult(JsonSerializer.Serialize(schema));
    }
}
