using System.Text;
using NadekoBot.AiAgent;

namespace NadekoBot.Modules.Utility.AiAgent.Adapters;

public sealed class SessionAiAdapter(
    ConversationWindowTracker tracker,
    IMessageSenderService sender) : IAiCoreToolGroup, INService
{
    public string GroupName => "session";
    public string GroupDescription => "Conversation lifecycle: ask the user a question, close the current session.";

    [AiTool(
        "close_session",
        "Close the current conversation session. Use when the user says goodbye, thanks, "
        + "or indicates they're done. After closing, the bot will stop listening for follow-up messages.")]
    public Task<string> CloseSession(AiToolContext ctx)
    {
        var closed = tracker.Close(ctx.User.Id, ctx.SourceChannel.Id);
        ctx.SessionClosed = true;
        return Task.FromResult(closed
            ? "Session closed. The user will need to mention the bot again to start a new conversation."
            : "No active session to close.");
    }

    [AiTool(
        "ask_user",
        "Ask the invoking user a clarifying question before proceeding. "
        + "The current session will pause after the question is sent. "
        + "When the user replies, a new session starts with the answer visible in channel history. "
        + "Use this when the user's request is ambiguous and you need more information to proceed correctly. "
        + "You can optionally provide multiple-choice options.")]
    [AiSystemGuidance("""
        ASKING FOR CLARIFICATION:
        When the user's request is ambiguous, use the ask_user tool to ask a clarifying question before proceeding.
        Limit questions to 2-3 per session unless absolutely necessary.
        If you can make a reasonable assumption, prefer acting over asking.
        """)]
    public async Task<string> AskUser(
        AiToolContext ctx,
        [AiParam("The question to ask the user")] string question,
        [AiParam("Optional list of choices. Displayed as a numbered list. The user can reply with a number or free-form text.")]
        List<string>? options = null)
    {
        if (string.IsNullOrWhiteSpace(question))
            throw ToolException.InvalidArgument("question cannot be empty.");

        var sb = new StringBuilder(question);

        if (options is { Count: > 0 })
        {
            sb.AppendLine();
            var i = 1;
            foreach (var opt in options)
            {
                if (string.IsNullOrWhiteSpace(opt))
                    continue;
                sb.AppendLine();
                sb.Append("**").Append(i).Append(".** ").Append(opt);
                i++;
            }
        }

        var eb = sender.CreateEmbed(ctx.Guild.Id)
            .WithPendingColor()
            .WithDescription(sb.ToString().TrimTo(4096));

        await ctx.SourceChannel.SendMessageAsync(embed: eb.Build());

        ctx.AskPending = true;

        return "Question sent. The session will now end. The user's reply will be available in channel history on the next invocation.";
    }
}
