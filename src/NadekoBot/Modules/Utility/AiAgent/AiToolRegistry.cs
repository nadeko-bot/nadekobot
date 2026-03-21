using System.Text.Json;
using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Utility.AiAgent;

/// <summary>
/// Registry implementation that discovers all IAiTool implementations via DI
/// and caches their OpenAI-compatible schemas
/// </summary>
public sealed class AiToolRegistry : IAiToolRegistry, INService
{
    private readonly Dictionary<string, IAiTool> _tools;
    private readonly List<JsonElement> _schemas;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AiToolRegistry(IEnumerable<IAiTool> tools)
    {
        _tools = tools.ToDictionary(t => t.Name);
        _schemas = _tools.Values.Select(BuildSchema).ToList();
    }

    /// <summary>
    /// Get all registered tools
    /// </summary>
    public IReadOnlyList<IAiTool> GetAllTools()
        => _tools.Values.ToList();

    /// <summary>
    /// Get a tool by name, or null if not found
    /// </summary>
    public IAiTool? GetTool(string name)
        => _tools.GetValueOrDefault(name);

    /// <summary>
    /// Get all tool schemas in OpenAI function-calling format
    /// </summary>
    public IReadOnlyList<JsonElement> GetToolSchemas()
        => _schemas;

    /// <summary>
    /// Get tool schemas filtered to a set of allowed tool names
    /// </summary>
    public IReadOnlyList<JsonElement> GetToolSchemas(IReadOnlySet<string> allowedTools)
        => _tools.Where(kv => allowedTools.Contains(kv.Key))
                 .Select(kv => BuildSchema(kv.Value))
                 .ToList();

    private static JsonElement BuildSchema(IAiTool tool)
    {
        var obj = new
        {
            type = "function",
            function = new
            {
                name = tool.Name,
                description = tool.Description,
                parameters = tool.ParameterSchema
            }
        };

        var json = JsonSerializer.Serialize(obj, _jsonOpts);
        return JsonDocument.Parse(json).RootElement.Clone();
    }
}
