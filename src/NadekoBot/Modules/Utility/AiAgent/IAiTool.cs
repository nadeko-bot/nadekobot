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
    /// Whether this is a data tool discovered via search_data_tools (true) or a core tool always loaded (false)
    /// </summary>
    bool IsDataTool => false;

    /// <summary>
    /// Optional system prompt guidance for the LLM on when and how to use this tool.
    /// Collected by SystemPromptBuilder and emitted in the TOOL USAGE slot.
    /// Tool guidance lives with the tool so it can never drift from the tool's actual behavior.
    /// </summary>
    string? SystemGuidance => null;

    /// <summary>
    /// Execute the tool and return a result string that will be fed back to the LLM
    /// </summary>
    Task<string> ExecuteAsync(AiToolContext context, JsonElement arguments);
}
