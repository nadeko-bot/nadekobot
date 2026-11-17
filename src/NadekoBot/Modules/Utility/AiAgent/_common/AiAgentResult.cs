namespace NadekoBot.Modules.Utility.AiAgent;

public sealed class AiAgentResult
{
    // Empty when the agent has nothing to add to what the member already sees.
    public required string Response { get; init; }

    public required int ToolCallCount { get; init; }

    public bool AskPending { get; init; }

    // A budget ended the request, and the response is the summary which the model wrote after it.
    public bool LimitReached { get; init; }
}

public enum AgentFailure
{
    Provider,
    EmptyReply,

    // The reply stopped at the token limit, so the bot did not run its tool calls.
    CutOff
}
