using System.Text.Json;

namespace NadekoBot.Modules.Utility.AiAgent.Tools;

/// <summary>
/// Closes the active conversation window for the invoking user.
/// Called when the user indicates they're done talking to the bot.
/// </summary>
public sealed class CloseSessionTool(ConversationWindowTracker tracker) : IAiTool, INService
{
    public string Name => "close_session";

    public string Description =>
        "Close the current conversation session. Use when the user says goodbye, thanks, " +
        "or indicates they're done. After closing, the bot will stop listening for follow-up messages.";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {}
        }
        """).RootElement.Clone();

    public Task<string> ExecuteAsync(AiToolContext context, JsonElement arguments)
    {
        var closed = tracker.Close(context.User.Id, context.SourceChannel.Id);
        context.SessionClosed = true;
        return Task.FromResult(closed
            ? "Session closed. The user will need to mention the bot again to start a new conversation."
            : "No active session to close.");
    }
}
