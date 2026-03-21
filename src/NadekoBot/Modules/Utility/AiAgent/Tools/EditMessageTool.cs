using System.Text.Json;

namespace NadekoBot.Modules.Utility.AiAgent.Tools;

/// <summary>
/// Edits a message that was sent by the bot. Cannot edit other users' messages.
/// </summary>
public sealed class EditMessageTool : IAiTool, INService
{
    public string Name => "edit_message";

    public string Description =>
        "Edit a message that was previously sent by the bot. " +
        "Only bot-sent messages can be edited. Maximum 2000 characters.";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "channel_id": {
                    "type": "string",
                    "description": "The ID of the channel containing the message"
                },
                "message_id": {
                    "type": "string",
                    "description": "The ID of the message to edit"
                },
                "text": {
                    "type": "string",
                    "description": "The new text content for the message (max 2000 chars)"
                }
            },
            "required": ["channel_id", "message_id", "text"]
        }
        """).RootElement.Clone();

    public async Task<string> ExecuteAsync(AiToolContext context, JsonElement arguments)
    {
        if (!arguments.TryGetProperty("channel_id", out var channelIdEl)
            || !ulong.TryParse(channelIdEl.GetString(), out var channelId))
            return "Error: channel_id is required and must be a valid ID.";

        if (!arguments.TryGetProperty("message_id", out var messageIdEl)
            || !ulong.TryParse(messageIdEl.GetString(), out var messageId))
            return "Error: message_id is required and must be a valid ID.";

        if (!arguments.TryGetProperty("text", out var textEl)
            || string.IsNullOrWhiteSpace(textEl.GetString()))
            return "Error: text is required and cannot be empty.";

        var text = textEl.GetString()!;
        if (text.Length > 2000)
            return "Error: Text exceeds Discord's 2000 character limit.";

        var channel = await context.Guild.GetTextChannelAsync(channelId);
        if (channel is null)
            return "Error: Channel not found.";

        var userPerms = context.User.GetPermissions(channel);
        if (!userPerms.ViewChannel)
            return "Error: You don't have permission to view that channel.";

        var message = await channel.GetMessageAsync(messageId);
        if (message is null)
            return "Error: Message not found.";

        if (message.Author.Id != (await context.Guild.GetCurrentUserAsync()).Id)
            return "Error: Can only edit messages sent by the bot.";

        if (message is not IUserMessage userMessage)
            return "Error: This message cannot be edited.";

        await userMessage.ModifyAsync(m => m.Content = text);
        return "Message edited successfully.";
    }
}
