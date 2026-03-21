using System.Text.Json;

namespace NadekoBot.Modules.Utility.AiAgent.Tools;

/// <summary>
/// Deletes a message. Bot's own messages are always deletable.
/// Other users' messages require the invoking user to have ManageMessages permission.
/// </summary>
public sealed class DeleteMessageTool : IAiTool, INService
{
    public string Name => "delete_message";

    public string Description =>
        "Delete a message by ID. The bot can always delete its own messages. " +
        "To delete another user's message, you must have the Manage Messages permission.";

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
                    "description": "The ID of the message to delete"
                }
            },
            "required": ["channel_id", "message_id"]
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

        var channel = await context.Guild.GetTextChannelAsync(channelId);
        if (channel is null)
            return "Error: Channel not found.";

        var userPerms = context.User.GetPermissions(channel);
        if (!userPerms.ViewChannel || !userPerms.ReadMessageHistory)
            return "Error: You don't have permission to view messages in that channel.";

        var message = await channel.GetMessageAsync(messageId);
        if (message is null)
            return "Error: Message not found.";

        var isBotMessage = message.Author.Id == (await context.Guild.GetCurrentUserAsync()).Id;
        if (!isBotMessage && !userPerms.ManageMessages)
            return "Error: You need Manage Messages permission to delete other users' messages.";

        await channel.DeleteMessageAsync(message);
        return "Message deleted successfully.";
    }
}
