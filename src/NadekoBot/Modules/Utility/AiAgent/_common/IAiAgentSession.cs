using System.Text.Json;
using OneOf;

namespace NadekoBot.Modules.Utility.AiAgent;

public interface IAiAgentSession
{
    Task<OneOf<AiAgentResult, AgentFailure>> RunAsync(
        AiAgentPrompt prompt,
        AiToolContext context,
        IReadOnlyList<IAiTool> tools,
        IReadOnlyList<JsonElement> toolSchemas,
        AiAgentConfig config,
        IAgentProgress? progress = null,
        CancellationToken ct = default);
}

// Ordered from the most shared part to the least shared part, which is the order the request sends them in.
public sealed record AiAgentPrompt(
    string System,
    string Context,
    ChannelHistoryFeed? History,
    string Turn,
    string Request);
