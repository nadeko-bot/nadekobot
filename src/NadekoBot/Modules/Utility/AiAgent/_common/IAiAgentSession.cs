using System.Text.Json;
using OneOf;
using OneOf.Types;

namespace NadekoBot.Modules.Utility.AiAgent;

// Repeats until the LLM gives a final text response, or the step limit is reached.
public interface IAiAgentSession
{
    Task<OneOf<AiAgentResult, Error<string>>> RunAsync(
        AiAgentPrompt prompt,
        AiToolContext context,
        IReadOnlyList<IAiTool> tools,
        IReadOnlyList<JsonElement> toolSchemas,
        AiAgentConfig config,
        CancellationToken ct = default);
}

// Ordered from the most shared part to the least shared part, which is the order the request sends them in.
public sealed record AiAgentPrompt(
    string System,
    string Context,
    ChannelHistoryFeed? History,
    string Turn);
