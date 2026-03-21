using System.Text;
using System.Text.Json;

namespace NadekoBot.Modules.Utility.AiAgent.Tools;

/// <summary>
/// Searches recent messages in a channel with optional text and user filters.
/// </summary>
public sealed class SearchMessagesTool : IAiTool, INService
{
    private const int DEFAULT_COUNT = 20;
    private const int MAX_COUNT = 50;
    private const int MAX_CONTENT_LENGTH = 300;

    public string Name => "search_messages";

    public string Description =>
        "Search recent messages in a channel. Optionally filter by text content (case-insensitive) " +
        $"and/or user ID. Returns up to {MAX_COUNT} matching messages with author, timestamp, and content.";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse($$"""
        {
            "type": "object",
            "properties": {
                "channel_id": {
                    "type": "string",
                    "description": "The ID of the channel to search in"
                },
                "query": {
                    "type": "string",
                    "description": "Optional text to search for (case-insensitive substring match)"
                },
                "user_id": {
                    "type": "string",
                    "description": "Optional user ID to filter messages by author"
                },
                "count": {
                    "type": "integer",
                    "description": "Maximum number of results to return (default {{DEFAULT_COUNT}}, max {{MAX_COUNT}})"
                }
            },
            "required": ["channel_id"]
        }
        """).RootElement.Clone();

    public async Task<string> ExecuteAsync(AiToolContext context, JsonElement arguments)
    {
        if (!arguments.TryGetProperty("channel_id", out var channelIdEl)
            || !ulong.TryParse(channelIdEl.GetString(), out var channelId))
            return "Error: channel_id is required and must be a valid ID.";

        var channel = await context.Guild.GetTextChannelAsync(channelId);
        if (channel is null)
            return "Error: Channel not found.";

        var userPerms = context.User.GetPermissions(channel);
        if (!userPerms.ViewChannel || !userPerms.ReadMessageHistory)
            return "Error: You don't have permission to read messages in that channel.";

        arguments.TryGetProperty("query", out var queryEl);
        var query = queryEl.ValueKind == JsonValueKind.String ? queryEl.GetString() : null;

        ulong? filterUserId = null;
        if (arguments.TryGetProperty("user_id", out var userIdEl)
            && ulong.TryParse(userIdEl.GetString(), out var uid))
            filterUserId = uid;

        var count = DEFAULT_COUNT;
        if (arguments.TryGetProperty("count", out var countEl) && countEl.TryGetInt32(out var c))
            count = Math.Clamp(c, 1, MAX_COUNT);

        // Fetch more than needed to account for filtering
        var fetchLimit = string.IsNullOrWhiteSpace(query) && filterUserId is null
            ? count
            : count * 5;
        fetchLimit = Math.Min(fetchLimit, 200);

        var messages = await channel.GetMessagesAsync(fetchLimit).FlattenAsync();

        var filtered = messages.Where(m =>
        {
            if (filterUserId.HasValue && m.Author.Id != filterUserId.Value)
                return false;

            if (!string.IsNullOrWhiteSpace(query)
                && (m.Content is null || !m.Content.Contains(query, StringComparison.OrdinalIgnoreCase)))
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
            if (content.Length > MAX_CONTENT_LENGTH)
                content = content[..MAX_CONTENT_LENGTH] + "...";

            sb.AppendLine($"[{msg.Timestamp:yyyy-MM-dd HH:mm:ss UTC}] {msg.Author.Username} (ID: {msg.Id}): {content}");
        }

        return sb.ToString();
    }
}
