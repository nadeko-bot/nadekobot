using System.Text.Json;

namespace NadekoBot.Modules.Utility.AiAgent;

// The session loop waits for these calls, so an implementation must return at once and do its I/O elsewhere.
public interface IAgentProgress
{
    void OnNote(string text);

    void OnToolCall(string name, JsonElement args, bool failed);
}
