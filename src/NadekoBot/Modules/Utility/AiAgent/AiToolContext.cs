namespace NadekoBot.Modules.Utility.AiAgent;

/// <summary>
/// Carries identity and authorization info for a single agent invocation.
/// Every tool checks this before acting - the AI cannot exceed the invoking user's permissions.
/// </summary>
public sealed class AiToolContext
{
    /// <summary>
    /// Guild where the agent was invoked
    /// </summary>
    public required IGuild Guild { get; init; }

    /// <summary>
    /// Channel where the agent was invoked
    /// </summary>
    public required ITextChannel SourceChannel { get; init; }

    /// <summary>
    /// User who triggered the agent - all permission checks use this identity
    /// </summary>
    public required IGuildUser User { get; init; }

    /// <summary>
    /// The original message that triggered the agent invocation
    /// </summary>
    public required IUserMessage TriggerMessage { get; init; }

    /// <summary>
    /// Token that fires when the user cancels the running agent session
    /// </summary>
    public required CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// Set by close_session tool to prevent the session from being re-opened after the agent responds
    /// </summary>
    public bool SessionClosed { get; set; }
}
