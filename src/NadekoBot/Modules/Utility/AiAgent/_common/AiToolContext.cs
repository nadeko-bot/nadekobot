namespace NadekoBot.Modules.Utility.AiAgent;

// Every tool checks this before it acts, so the agent can never exceed the permissions of the user.
public sealed class AiToolContext
{
    public required IGuild Guild { get; init; }

    public required ITextChannel SourceChannel { get; init; }

    // All the permission checks use this identity.
    public required IGuildUser User { get; init; }

    public required IUserMessage TriggerMessage { get; init; }

    public required CancellationToken CancellationToken { get; init; }

    // Set by close_session, to stop the session from opening again after the agent responds.
    public bool SessionClosed { get; set; }

    // Set by ask_user, to end the loop and wait for the reply of the user.
    public bool AskPending { get; set; }
}
