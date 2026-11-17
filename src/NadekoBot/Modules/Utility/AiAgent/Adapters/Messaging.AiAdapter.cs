using System.Text;
using System.Text.RegularExpressions;
using Discord;
using NadekoBot.AiAgent;

namespace NadekoBot.Modules.Utility.AiAgent.Adapters;

public sealed partial class MessagingAiAdapter(IMessageSenderService sender, IntentState intents, AgentLimiter limiter)
    : IAiCoreToolGroup, INService
{
    public string GroupName => "messaging";
    public string GroupDescription => "Send, edit, read, delete, and search Discord messages.";

    private const int SEARCH_DEFAULT_COUNT = 20;
    private const int SEARCH_MAX_COUNT = 50;
    private const int SEARCH_MAX_CONTENT_LENGTH = 300;
    private const string TEXT_UNAVAILABLE = "not available, because the bot does not have the Message Content intent";
    private const int MESSAGES_PER_PAGE = 100;

    public const int MAX_SEND_PARTS = 4;

    [GeneratedRegex(@"<#(\d+)>")]
    private static partial Regex ChannelMentionRegex();

    [AiTool(
        "send_message",
        "Send a message to a Discord channel. "
        + "You can specify the channel by ID, mention (like <#123456>), or name (like #general). "
        + "The message will appear as sent by the bot, not the user. "
        + "The `message` parameter is plain text by default, but can also be a JSON document describing one or more "
        + "rich embeds (see system guidance for the schema). Long messages split automatically on word boundaries.")]
    [AiSystemGuidance(SystemGuidanceText.SendMessage)]
    public async Task<string> SendMessage(
        AiToolContext ctx,
        [AiParam("The target channel - can be an ID, a mention like <#123456>, or a name like #general")]
        string channel,
        [AiParam("Plain text, OR a JSON object with `content` and `embeds[]` (see system guidance).")]
        string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw ToolException.InvalidArgument("`message` must not be empty.");

        var bot = await ctx.Guild.GetCurrentUserAsync();
        var (ch, access) = await ResolveChannelInternalAsync(ctx, channel, bot)
                           ?? throw ToolException.NotFound("Channel not found. Make sure the channel exists and is a text channel.");

        var smart = SmartText.CreateFrom(message);
        if (AgentPermissions.CheckSend(access, ch.SlowModeInterval > 0, smart is SmartEmbedTextArray { Embeds: { Length: > 0 } }) is { } denied)
            throw denied;

        // The split of ResponseBuilder, so each Discord message of a long text takes one send of the server budget.
        var splitText = smart switch
        {
            SmartPlainText plain => plain.Text,
            SmartEmbedTextArray array => array.Content,
            _ => null
        };
        var parts = splitText is { Length: > MessageSplitter.MAX_PLAIN_TEXT_LENGTH }
            ? MessageSplitter.Count(splitText, MessageSplitter.MAX_PLAIN_TEXT_LENGTH)
            : 1;
        if (parts > MAX_SEND_PARTS)
            throw ToolException.InvalidArgument(
                $"The message is too long: it needs {parts} Discord messages, and the limit is {MAX_SEND_PARTS}. Make it shorter.");

        await TakeInternalAsync(limiter.TakeSendAsync(ctx.Guild.Id, parts, ctx.CancellationToken));

        // A split message which fails in the middle still leaves its first parts, and the member may delete them.
        var sentIds = new List<ulong>(parts);
        try
        {
            await sender.Response(ch).Text(smart).Split().SendAsync(sentIds);
        }
        catch (Exception ex) when (sentIds.Count > 0 && ex is not OperationCanceledException)
        {
            Log.Warning(ex, "AI agent posted only part of a message in channel {ChannelId}", ch.Id);

            // The member saw the first parts, so an empty last reply is fine.
            ctx.Session.AnythingShown = true;
            throw new ToolException("partly_sent",
                $"Only the first {sentIds.Count} of {parts} parts were posted. Do not send the whole text again.");
        }
        finally
        {
            foreach (var id in sentIds)
            {
                ctx.SentMessageIds[id] = true;
                ctx.NoteOutput(ch.Id, id);
            }
        }

        return $"Message sent to #{ch.Name} successfully.";
    }

    [AiTool(
        "edit_message",
        "Edit a message that was previously sent by the bot. "
        + "Only bot-sent messages can be edited. A message which you did not send for the member "
        + "needs the Manage Messages permission. Maximum 2000 characters.")]
    public async Task<string> EditMessage(
        AiToolContext ctx,
        [AiParam("The ID of the channel containing the message")]
        ulong channelId,
        [AiParam("The ID of the message to edit")]
        ulong messageId,
        [AiParam("The new text content for the message (max 2000 chars)")]
        string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw ToolException.InvalidArgument("text is required and cannot be empty.");
        if (text.Length > DiscordConfig.MaxMessageSize)
            throw ToolException.InvalidArgument("Text exceeds Discord's 2000 character limit.");

        var bot = await ctx.Guild.GetCurrentUserAsync();
        var (ch, access) = await GetChannelInternalAsync(ctx, channelId, bot);
        if (AgentPermissions.CheckEdit(access, ctx.SentMessageIds.ContainsKey(messageId)) is { } denied)
            throw denied;

        await TakeInternalAsync(limiter.TakeRestAsync(ctx.Guild.Id, 1, ctx.CancellationToken));
        var msg = await ch.GetMessageAsync(messageId)
                  ?? throw ToolException.NotFound("Message not found.");

        if (msg.Author.Id != bot.Id)
            throw ToolException.Forbidden("Can only edit messages sent by the bot.");

        if (msg is not IUserMessage userMessage)
            throw ToolException.Forbidden("This message cannot be edited.");

        await TakeInternalAsync(limiter.TakeRestAsync(ctx.Guild.Id, 1, ctx.CancellationToken));
        await userMessage.ModifyAsync(m => m.Content = text);
        return "Message edited successfully.";
    }

    [AiTool(
        "get_message",
        "Fetch the content of a Discord message by channel ID and message ID. "
        + "Returns the message text, author, and timestamp.")]
    public async Task<string> GetMessage(
        AiToolContext ctx,
        [AiParam("The ID of the channel containing the message")]
        ulong channelId,
        [AiParam("The ID of the message to fetch")]
        ulong messageId)
    {
        var bot = await ctx.Guild.GetCurrentUserAsync();
        var (ch, access) = await GetChannelInternalAsync(ctx, channelId, bot);
        if (AgentPermissions.CheckRead(access) is { } denied)
            throw denied;

        await TakeInternalAsync(limiter.TakeRestAsync(ctx.Guild.Id, 1, ctx.CancellationToken));
        var msg = await ch.GetMessageAsync(messageId)
                  ?? throw ToolException.NotFound("Message not found.");

        var sb = new StringBuilder();
        sb.AppendLine($"Author: {msg.Author.Username}");
        sb.AppendLine($"Timestamp: {msg.Timestamp:yyyy-MM-dd HH:mm:ss UTC}");

        if (!string.IsNullOrWhiteSpace(msg.Content))
            sb.AppendLine($"Content:\n{msg.Content}");
        else if (LayoutText.Read(msg.Components) is { Length: > 0 } layout)
            sb.AppendLine($"Content:\n{layout}");
        else if (!intents.MessageContent && msg.Author.Id != bot.Id)
            sb.AppendLine($"Content: {TEXT_UNAVAILABLE}");

        foreach (var emb in msg.Embeds)
        {
            if (!string.IsNullOrWhiteSpace(emb.Title))
                sb.AppendLine($"Embed Title: {emb.Title}");
            if (!string.IsNullOrWhiteSpace(emb.Description))
                sb.AppendLine($"Embed Description:\n{emb.Description}");
            foreach (var field in emb.Fields)
                sb.AppendLine($"Embed Field [{field.Name}]: {field.Value}");
            if (emb.Footer.HasValue && !string.IsNullOrWhiteSpace(emb.Footer.Value.Text))
                sb.AppendLine($"Embed Footer: {emb.Footer.Value.Text}");
        }

        return sb.ToString();
    }

    [AiTool(
        "delete_message",
        "Delete a message by ID. The member can delete their own messages and the ones which you sent for them. "
        + "Any other message needs the Manage Messages permission.")]
    public async Task<string> DeleteMessage(
        AiToolContext ctx,
        [AiParam("The ID of the channel containing the message")]
        ulong channelId,
        [AiParam("The ID of the message to delete")]
        ulong messageId)
    {
        var bot = await ctx.Guild.GetCurrentUserAsync();
        var (ch, access) = await GetChannelInternalAsync(ctx, channelId, bot);
        var sentForMember = ctx.SentMessageIds.ContainsKey(messageId);
        if (AgentPermissions.CheckFind(access, sentForMember) is { } hidden)
            throw hidden;

        await TakeInternalAsync(limiter.TakeRestAsync(ctx.Guild.Id, 1, ctx.CancellationToken));
        var msg = await ch.GetMessageAsync(messageId)
                  ?? throw ToolException.NotFound("Message not found.");

        // A join or pin notice has the member as its author, but the member cannot delete it.
        if (AgentPermissions.CheckDelete(
                access,
                msg.Author.Id == ctx.User.Id && msg.Source == MessageSource.User,
                msg.Author.Id == bot.Id,
                sentForMember) is { } denied)
            throw denied;

        await TakeInternalAsync(limiter.TakeRestAsync(ctx.Guild.Id, 1, ctx.CancellationToken));
        await ch.DeleteMessageAsync(msg);
        return "Message deleted successfully.";
    }

    [AiTool(
        "search_messages",
        "Search recent messages in a channel. Optionally filter by text content (case-insensitive) "
        + "and/or user ID. Returns matching messages with author, timestamp, and content.")]
    public async Task<string> SearchMessages(
        AiToolContext ctx,
        [AiParam("The ID of the channel to search in")]
        ulong channelId,
        [AiParam("Optional text to search for (case-insensitive substring match)")]
        string? query = null,
        [AiParam("Optional user ID to filter messages by author")]
        ulong? userId = null,
        [AiParam("Maximum number of results (default 20, max 50)")]
        int count = SEARCH_DEFAULT_COUNT)
    {
        var bot = await ctx.Guild.GetCurrentUserAsync();
        var (ch, access) = await GetChannelInternalAsync(ctx, channelId, bot);
        if (AgentPermissions.CheckRead(access) is { } denied)
            throw denied;

        if (!intents.MessageContent && !string.IsNullOrWhiteSpace(query))
            throw ToolException.Forbidden($"Text search is {TEXT_UNAVAILABLE}. Search by user ID instead.");

        count = Math.Clamp(count, 1, SEARCH_MAX_COUNT);

        var fetchLimit = string.IsNullOrWhiteSpace(query) && userId is null
            ? count
            : count * 5;
        fetchLimit = Math.Min(fetchLimit, 200);

        // Discord returns at most 100 messages for each request.
        var pages = (fetchLimit + MESSAGES_PER_PAGE - 1) / MESSAGES_PER_PAGE;
        await TakeInternalAsync(limiter.TakeRestAsync(ctx.Guild.Id, pages, ctx.CancellationToken));
        var messages = await ch.GetMessagesAsync(fetchLimit).FlattenAsync();

        var filtered = messages.Where(m =>
        {
            if (userId.HasValue && m.Author.Id != userId.Value)
                return false;

            if (!string.IsNullOrWhiteSpace(query)
                && (m.Content is null || !m.Content.Contains(query, StringComparison.InvariantCultureIgnoreCase)))
                return false;

            return true;
        }).Take(count).ToList();

        if (filtered.Count == 0)
            return "No messages found matching the criteria.";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {filtered.Count} message(s):");

        foreach (var msg in filtered)
        {
            var content = msg.Content ?? string.Empty;
            if (content.Length > SEARCH_MAX_CONTENT_LENGTH)
                content = content[..SEARCH_MAX_CONTENT_LENGTH] + "...";

            sb.AppendLine($"[{msg.Timestamp:yyyy-MM-dd HH:mm:ss UTC}] {msg.Author.Username} (ID: {msg.Id}): {content}");
        }

        return sb.ToString();
    }

    private static async Task TakeInternalAsync(Task<bool> take)
    {
        if (!await take)
            throw new ToolException("busy", "The agents in this server used Discord too much just now. Do not try again in this request.");
    }

    private static async Task<(ITextChannel Channel, AgentChannelAccess Access)> GetChannelInternalAsync(
        AiToolContext ctx,
        ulong channelId,
        IGuildUser bot)
        => VisibleInternal(ctx, bot, await ctx.Guild.GetTextChannelAsync(channelId)) ?? throw AgentPermissions.ChannelNotFound();

    private static (ITextChannel Channel, AgentChannelAccess Access)? VisibleInternal(AiToolContext ctx, IGuildUser bot, ITextChannel? ch)
        => ch is not null && AgentPermissions.Access(ctx.User, bot, ch, ctx.SourceChannel.Id) is { } access ? (ch, access) : null;

    // A hidden channel with the same name must not hide a visible one.
    private static async Task<(ITextChannel Channel, AgentChannelAccess Access)?> ResolveChannelInternalAsync(
        AiToolContext ctx,
        string input,
        IGuildUser bot)
    {
        input = input.Trim();

        var mentionMatch = ChannelMentionRegex().Match(input);
        if (mentionMatch.Success && ulong.TryParse(mentionMatch.Groups[1].Value, out var mentionId))
            return VisibleInternal(ctx, bot, await ctx.Guild.GetTextChannelAsync(mentionId));

        if (ulong.TryParse(input, out var rawId))
            return VisibleInternal(ctx, bot, await ctx.Guild.GetTextChannelAsync(rawId));

        var name = input.TrimStart('#');
        var channels = await ctx.Guild.GetTextChannelsAsync();
        foreach (var c in channels)
        {
            if (string.Equals(c.Name, name, StringComparison.InvariantCultureIgnoreCase) && VisibleInternal(ctx, bot, c) is { } found)
                return found;
        }

        return null;
    }
}
