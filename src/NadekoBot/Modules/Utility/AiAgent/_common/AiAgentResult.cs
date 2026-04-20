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

    /// <summary>
    /// Whether the agent paused to ask the user a question via ask_user tool
    /// </summary>
    public bool AskPending { get; init; }
}
