using System.Text;
using System.Text.Json;

namespace NadekoBot.Modules.Utility.AiAgent.Tools;

/// <summary>
/// Lists all text channels in the guild that the invoking user can see, grouped by category.
/// </summary>
public sealed class ListChannelsTool : IAiTool, INService
{
    public string Name => "list_channels";

    public string Description =>
        "List all channels in the server that you can see, grouped by category. " +
        "Optionally filter to a specific category by name. " +
        "Returns channel ID, name, and type (text, voice, forum, stage).";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "category": {
                    "type": "string",
                    "description": "Optional category name to filter by (case-insensitive)"
                }
            }
        }
        """).RootElement.Clone();

    public async Task<string> ExecuteAsync(AiToolContext context, JsonElement arguments)
    {
        arguments.TryGetProperty("category", out var catEl);
        var categoryFilter = catEl.ValueKind == JsonValueKind.String ? catEl.GetString()?.Trim() : null;

        var allChannels = await context.Guild.GetChannelsAsync();

        var categories = allChannels
            .OfType<ICategoryChannel>()
            .OrderBy(c => c.Position)
            .ToList();

        var channels = allChannels
            .Where(c => c is not ICategoryChannel)
            .OfType<IGuildChannel>()
            .Where(c => context.User.GetPermissions(c).ViewChannel)
            .ToList();

        var grouped = channels
            .GroupBy(c => c is INestedChannel nc ? nc.CategoryId : null)
            .OrderBy(g =>
            {
                if (g.Key is null) return -1;
                var cat = categories.FirstOrDefault(c => c.Id == g.Key);
                return cat?.Position ?? int.MaxValue;
            })
            .ToList();

        var sb = new StringBuilder();

        foreach (var group in grouped)
        {
            string catName;
            if (group.Key is null)
                catName = "(No Category)";
            else
                catName = categories.FirstOrDefault(c => c.Id == group.Key)?.Name ?? "Unknown";

            if (!string.IsNullOrWhiteSpace(categoryFilter)
                && !catName.Contains(categoryFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            sb.AppendLine($"=== {catName} ===");

            foreach (var ch in group.OrderBy(c => c.Position))
            {
                var type = ch switch
                {
                    IStageChannel => "stage",
                    IVoiceChannel => "voice",
                    IForumChannel => "forum",
                    ITextChannel => "text",
                    _ => "other"
                };

                sb.AppendLine($"  #{ch.Name} (ID: {ch.Id}, {type})");
            }
        }

        if (sb.Length == 0)
        {
            return string.IsNullOrWhiteSpace(categoryFilter)
                ? "No visible channels found."
                : $"No channels found in category matching '{categoryFilter}'.";
        }

        return sb.ToString();
    }
}
