using System.Text;
using NadekoBot.AiAgent;

namespace NadekoBot.Modules.Utility.AiAgent.Adapters;

public sealed class SessionAiAdapter(
    ConversationWindowTracker tracker,
    AgentMessageTracker agentMessages,
    IMessageSenderService sender,
    AgentLimiter limiter,
    AiAgentConfigService configService) : IAiCoreToolGroup, INService
{
    public const string ASK_TOOL = "ask_user";

    // With these limits the ping, the question and its options fit one message, so the member and the answer request see the same text.
    private const int MAX_QUESTION_CHARS = 1000;
    private const int MAX_OPTIONS = 10;
    private const int MAX_OPTION_CHARS = 80;

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
        ASK_TOOL,
        "Ask the member one question before you go on. The request ends after the question, and no other call of the "
        + "same reply runs. The member's next message starts a new request, which shows your question and their answer. "
        + "You can give a list of choices.")]
    [AiSystemGuidance("""
        ASKING FOR CLARIFICATION:
        When the request is not clear, call ask_user alone and ask one question. Do not ask again in the request
        which answers it. When you can make a reasonable assumption, act instead of asking.
        """)]
    public async Task<string> AskUser(
        AiToolContext ctx,
        [AiParam("The question to ask the user")] string question,
        [AiParam("Optional list of up to 10 choices of 80 characters. Displayed as a numbered list. The user can reply with a number or free-form text.")]
        List<string>? options = null)
    {
        if (string.IsNullOrWhiteSpace(question))
            throw ToolException.InvalidArgument("question cannot be empty.");

        if (ctx.EarlierRequest is not null)
            return "Error: not asked. The member's message answers your earlier question. "
                   + "Do what you can with it, or tell the member what is missing.";

        var text = BuildQuestion(question, options);

        // Without the permission the ping would arrive without the question, or the call would fail with 403.
        var bot = await ctx.Guild.GetCurrentUserAsync();
        if (!AgentPermissions.CanSend(bot, ctx.SourceChannel))
            throw new ToolException("bot_missing_permission", "The bot cannot send messages in this channel, so it cannot ask.");

        var perms = AgentPermissions.PermissionsIn(bot, ctx.SourceChannel);

        if (!await limiter.TakeSendAsync(ctx.Guild.Id, 1, ctx.CancellationToken))
            throw new ToolException("busy", "The agents in this server sent too many messages just now. Do not ask in this request.");

        // The ping tells the member that the agent waits for them; it mentions nobody else.
        var mentions = new AllowedMentions { UserIds = [ctx.User.Id] };
        var button = ctx.SessionClosed ? null : ctx.EndSessionComponent;
        IUserMessage sent;
        if (configService.Data.UseEmbed && perms.EmbedLinks)
        {
            var eb = sender.CreateEmbed(ctx.Guild.Id)
                .WithPendingColor()
                .WithDescription(text);
            sent = await ctx.SourceChannel.SendMessageAsync(
                ctx.User.Mention,
                embed: eb.Build(),
                allowedMentions: mentions,
                components: button);
        }
        else
        {
            sent = await ctx.SourceChannel.SendMessageAsync(
                ctx.User.Mention + "\n" + text,
                allowedMentions: mentions,
                components: button);
        }

        agentMessages.Add(sent.Id);
        if (button is not null)
            ctx.QuestionButtonId = sent.Id;

        // The options go with the question, because an answer such as "2" means nothing without them.
        ctx.AskPending = true;
        ctx.Question = text;

        return "Question sent. The request ends now. The member's next message starts a new request with your question.";
    }

    public static string BuildQuestion(string question, List<string>? options)
    {
        var sb = new StringBuilder(CutInternal(question.Trim(), MAX_QUESTION_CHARS));
        if (options is not { Count: > 0 })
            return sb.ToString();

        var number = 0;
        foreach (var option in options)
        {
            if (string.IsNullOrWhiteSpace(option))
                continue;

            if (++number > MAX_OPTIONS)
                break;

            if (number == 1)
                sb.AppendLine();

            sb.AppendLine();
            sb.Append("**").Append(number).Append(".** ").Append(CutInternal(option.Trim(), MAX_OPTION_CHARS));
        }

        return sb.ToString();
    }

    private static string CutInternal(string text, int max)
        => text.Length <= max
            ? text
            : string.Concat(text.AsSpan(0, char.IsHighSurrogate(text[max - 1]) ? max - 1 : max), "...");
}
