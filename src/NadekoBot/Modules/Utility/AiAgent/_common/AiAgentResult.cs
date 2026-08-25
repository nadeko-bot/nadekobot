namespace NadekoBot.Modules.Utility.AiAgent;

public sealed class AiAgentResult
{
    public required string Response { get; init; }

    public required int ToolCallCount { get; init; }

    public required bool WasCancelled { get; init; }

    // The agent stopped to ask a question and waits for the reply of the user.
    public bool AskPending { get; init; }
}
