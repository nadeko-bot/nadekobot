using System.Text.Json;
using OneOf;
using OneOf.Types;

namespace NadekoBot.Modules.Utility.AiAgent;

// Repeats until the LLM gives a final text response, or the step limit is reached.
public interface IAiAgentSession
{
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
