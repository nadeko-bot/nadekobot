using System.Text.Json;
using OneOf;
using OneOf.Types;

namespace NadekoBot.Modules.Utility.AiAgent;

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
        Func<string?>? channelHistoryProvider,
        CancellationToken ct = default);
}
