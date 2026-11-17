using System.Text.Json;

namespace NadekoBot.Modules.Utility.AiAgent;

public interface IAiTool
{
    string Name { get; }

    string Description { get; }

    // OpenAI function-calling format.
    JsonElement ParameterSchema { get; }

    // True for a tool which search_data_tools discovers, false for a tool which is always loaded.
    bool IsDataTool => false;

    // Lives with the tool, so it can never drift from what the tool does.
    string? SystemGuidance => null;

    Task<string> ExecuteAsync(AiToolContext context, JsonElement arguments);
}
