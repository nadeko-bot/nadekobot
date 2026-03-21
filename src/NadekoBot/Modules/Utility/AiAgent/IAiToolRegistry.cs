using System.Text.Json;

namespace NadekoBot.Modules.Utility.AiAgent;

/// <summary>
/// Collects all registered AI tools and provides them in OpenAI-compatible format
/// </summary>
public interface IAiToolRegistry
{
    /// <summary>
    /// Get all registered tools
    /// </summary>
    IReadOnlyList<IAiTool> GetAllTools();

    /// <summary>
    /// Get a tool by name
    /// </summary>
    IAiTool? GetTool(string name);

    /// <summary>
    /// Get tool definitions in OpenAI function-calling format
    /// </summary>
    IReadOnlyList<JsonElement> GetToolSchemas();

    /// <summary>
    /// Get tool definitions filtered to a set of allowed names
    /// </summary>
    IReadOnlyList<JsonElement> GetToolSchemas(IReadOnlySet<string> allowedTools);
}
