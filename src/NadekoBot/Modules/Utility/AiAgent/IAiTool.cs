using System.Text.Json;

namespace NadekoBot.Modules.Utility.AiAgent;

/// <summary>
/// A discrete action that the AI agent can invoke during a ReAct loop.
/// Each tool declares its OpenAI-compatible schema and executes within a permission-scoped context.
/// </summary>
public interface IAiTool
{
    /// <summary>
    /// Tool name exposed to the LLM (e.g. "send_message", "get_message")
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Human-readable description sent to the LLM so it understands when to use this tool
    /// </summary>
    string Description { get; }

    /// <summary>
    /// JSON Schema describing the parameters object for this tool (OpenAI function-calling format)
    /// </summary>
    JsonElement ParameterSchema { get; }

    /// <summary>
    /// Execute the tool and return a result string that will be fed back to the LLM
    /// </summary>
    Task<string> ExecuteAsync(AiToolContext context, JsonElement arguments);
}
