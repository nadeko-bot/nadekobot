using System.Text.Json;
using OneOf;
using OneOf.Types;

namespace NadekoBot.Modules.Utility.AiAgent;

/// <summary>
/// Result of an agent session execution
/// </summary>
public sealed class AiAgentResult
{
    /// <summary>
    /// The final text response from the LLM
    /// </summary>
    public required string Response { get; init; }

    /// <summary>
    /// Number of tool calls made during the session
    /// </summary>
    public required int ToolCallCount { get; init; }

    /// <summary>
    /// Whether the session was cancelled by the user
    /// </summary>
    public required bool WasCancelled { get; init; }
}

/// <summary>
/// Runs the ReAct agent loop: sends prompt + tools to the LLM, executes tool calls,
/// feeds results back, and repeats until the LLM produces a final text response or the step limit is hit.
/// </summary>
public interface IAiAgentSession
{
    /// <summary>
    /// Execute the agent loop for a user prompt
    /// </summary>
    Task<OneOf<AiAgentResult, Error<string>>> RunAsync(
        string userPrompt,
        AiToolContext context,
        IReadOnlyList<IAiTool> tools,
        IReadOnlyList<JsonElement> toolSchemas,
        AiAgentConfig config,
        string systemPrompt,
        string? channelHistory,
        CancellationToken ct = default);
}
