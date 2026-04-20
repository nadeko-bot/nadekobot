using System.Text.Json;

namespace NadekoBot.Modules.Utility.AiAgent;

/// <summary>
/// Collects all registered AI tools and provides them in OpenAI-compatible format
/// </summary>
public interface IAiToolRegistry
{
    IReadOnlyList<IAiTool> GetAllTools();
    IAiTool? GetTool(string name);
    IReadOnlyList<JsonElement> GetToolSchemas();
    IReadOnlyList<JsonElement> GetAllToolSchemas();
    IReadOnlyList<JsonElement> GetToolSchemas(IReadOnlySet<string> allowedTools);
    IReadOnlyDictionary<string, IAiTool> GetDataTools();
}
