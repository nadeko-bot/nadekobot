using System.Text.Json;
using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Utility.AiAgent;

// Discovers the tools through DI and caches their schemas.
public sealed class AiToolRegistry : IAiToolRegistry, INService
{
    private readonly Dictionary<string, IAiTool> _tools;
    private readonly Dictionary<string, IAiTool> _dataTools;
    private readonly IAiTool[] _allTools;
    private readonly List<JsonElement> _coreSchemas;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AiToolRegistry(IEnumerable<IAiTool> tools)
    {
        _tools = tools.ToDictionary(t => t.Name);
        _dataTools = _tools.Where(kv => kv.Value.IsDataTool).ToDictionary(kv => kv.Key, kv => kv.Value);
        _allTools = _tools.Values.ToArray();
        _coreSchemas = _tools.Values.Where(t => !t.IsDataTool).Select(BuildSchema).ToList();
    }

    public IReadOnlyList<IAiTool> GetAllTools()
        => _allTools;

    public IAiTool? GetTool(string name)
        => _tools.GetValueOrDefault(name);

    public IReadOnlyList<JsonElement> GetToolSchemas()
        => _coreSchemas;

    public IReadOnlyList<JsonElement> GetToolSchemas(IReadOnlySet<string> allowedTools)
        => _tools.Where(kv => allowedTools.Contains(kv.Key))
                 .Select(kv => BuildSchema(kv.Value))
                 .ToList();

    public IReadOnlyDictionary<string, IAiTool> GetDataTools()
        => _dataTools;

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
