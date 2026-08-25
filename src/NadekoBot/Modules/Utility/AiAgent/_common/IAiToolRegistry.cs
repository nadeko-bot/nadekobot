using System.Text.Json;

namespace NadekoBot.Modules.Utility.AiAgent;

public interface IAiToolRegistry
{
    IReadOnlyList<IAiTool> GetAllTools();
    IAiTool? GetTool(string name);
    IReadOnlyList<JsonElement> GetToolSchemas();
    IReadOnlyList<JsonElement> GetToolSchemas(IReadOnlySet<string> allowedTools);
    IReadOnlyDictionary<string, IAiTool> GetDataTools();
}
