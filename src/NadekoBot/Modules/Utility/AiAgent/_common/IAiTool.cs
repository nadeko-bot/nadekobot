using System.Text.Json;

namespace NadekoBot.Modules.Utility.AiAgent;

// One action the agent can invoke during a ReAct loop.
public interface IAiTool
{
    // Exposed to the LLM, for example "send_message".
    string Name { get; }

    // Tells the LLM when to use the tool.
    string Description { get; }

    // OpenAI function-calling format.
    JsonElement ParameterSchema { get; }

    // True for a tool which search_data_tools discovers, false for a tool which is always loaded.
    bool IsDataTool => false;

    // Lives with the tool, so it can never drift from what the tool does.
    string? SystemGuidance => null;

    // The result string goes back to the LLM.
    Task<string> ExecuteAsync(AiToolContext context, JsonElement arguments);
}
