using System.Text;
using System.Text.RegularExpressions;
using Discord;
using NadekoBot.AiAgent;

namespace NadekoBot.Modules.Utility.AiAgent.Adapters;

/// <summary>
/// Send / edit / read / delete / search Discord messages.
/// </summary>
public sealed partial class MessagingAiAdapter(IMessageSenderService sender) : IAiCoreToolGroup, INService
{
    public string GroupName => "messaging";
    public string GroupDescription => "Send, edit, read, delete, and search Discord messages.";

    private const int SEARCH_DEFAULT_COUNT = 20;
    private const int SEARCH_MAX_COUNT = 50;
    private const int SEARCH_MAX_CONTENT_LENGTH = 300;

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

        var ch = await ResolveChannelInternalAsync(ctx, channel)
                 ?? throw ToolException.NotFound("Channel not found. Make sure the channel exists and is a text channel.");

        var perms = ctx.User.GetPermissions(ch);
        if (!perms.SendMessages)
            throw ToolException.MissingPermission("SendMessages");

        var smart = SmartText.CreateFrom(message);
        if (smart is SmartEmbedTextArray && !perms.EmbedLinks)
            throw ToolException.MissingPermission("EmbedLinks");

        await sender.Response(ch).Text(smart).Split().SendAsync();
        return $"Message sent to #{ch.Name} successfully.";
    }

    [AiTool(
        "edit_message",
        "Edit a message that was previously sent by the bot. "
        + "Only bot-sent messages can be edited. Maximum 2000 characters.")]
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
        if (text.Length > 2000)
            throw ToolException.InvalidArgument("Text exceeds Discord's 2000 character limit.");

        var ch = await ctx.Guild.GetTextChannelAsync(channelId)
                 ?? throw ToolException.NotFound("Channel not found.");

        if (!ctx.User.GetPermissions(ch).ViewChannel)
            throw ToolException.MissingPermission("ViewChannel");

        var msg = await ch.GetMessageAsync(messageId)
                  ?? throw ToolException.NotFound("Message not found.");

        var bot = await ctx.Guild.GetCurrentUserAsync();
        if (msg.Author.Id != bot.Id)
            throw ToolException.Forbidden("Can only edit messages sent by the bot.");

        if (msg is not IUserMessage userMessage)
            throw ToolException.Forbidden("This message cannot be edited.");

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
        var ch = await ctx.Guild.GetTextChannelAsync(channelId)
                 ?? throw ToolException.NotFound("Channel not found.");

        var perms = ctx.User.GetPermissions(ch);
        if (!perms.ViewChannel || !perms.ReadMessageHistory)
            throw ToolException.MissingPermission("ReadMessageHistory");

        var msg = await ch.GetMessageAsync(messageId)
                  ?? throw ToolException.NotFound("Message not found.");

        var sb = new StringBuilder();
        sb.AppendLine($"Author: {msg.Author.Username}");
        sb.AppendLine($"Timestamp: {msg.Timestamp:yyyy-MM-dd HH:mm:ss UTC}");

        if (!string.IsNullOrWhiteSpace(msg.Content))
            sb.AppendLine($"Content:\n{msg.Content}");

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
        "Delete a message by ID. The bot can always delete its own messages. "
        + "To delete another user's message, you must have the Manage Messages permission.")]
    public async Task<string> DeleteMessage(
        AiToolContext ctx,
        [AiParam("The ID of the channel containing the message")]
        ulong channelId,
        [AiParam("The ID of the message to delete")]
        ulong messageId)
    {
        var ch = await ctx.Guild.GetTextChannelAsync(channelId)
                 ?? throw ToolException.NotFound("Channel not found.");

        var perms = ctx.User.GetPermissions(ch);
        if (!perms.ViewChannel || !perms.ReadMessageHistory)
            throw ToolException.MissingPermission("ReadMessageHistory");

        var msg = await ch.GetMessageAsync(messageId)
                  ?? throw ToolException.NotFound("Message not found.");

        var bot = await ctx.Guild.GetCurrentUserAsync();
        var isBotMessage = msg.Author.Id == bot.Id;
        if (!isBotMessage && !perms.ManageMessages)
            throw ToolException.MissingPermission("ManageMessages");

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
        var ch = await ctx.Guild.GetTextChannelAsync(channelId)
                 ?? throw ToolException.NotFound("Channel not found.");

        var perms = ctx.User.GetPermissions(ch);
        if (!perms.ViewChannel || !perms.ReadMessageHistory)
            throw ToolException.MissingPermission("ReadMessageHistory");

        count = Math.Clamp(count, 1, SEARCH_MAX_COUNT);

        var fetchLimit = string.IsNullOrWhiteSpace(query) && userId is null
            ? count
            : count * 5;
        fetchLimit = Math.Min(fetchLimit, 200);

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

    private static async Task<ITextChannel?> ResolveChannelInternalAsync(AiToolContext ctx, string input)
    {
        input = input.Trim();

        var mentionMatch = ChannelMentionRegex().Match(input);
        if (mentionMatch.Success && ulong.TryParse(mentionMatch.Groups[1].Value, out var mentionId))
            return await ctx.Guild.GetTextChannelAsync(mentionId);

        if (ulong.TryParse(input, out var rawId))
            return await ctx.Guild.GetTextChannelAsync(rawId);

        var name = input.TrimStart('#');
        var channels = await ctx.Guild.GetTextChannelsAsync();
        foreach (var c in channels)
        {
            if (string.Equals(c.Name, name, StringComparison.InvariantCultureIgnoreCase))
                return c;
        }
        return null;
    }
}
