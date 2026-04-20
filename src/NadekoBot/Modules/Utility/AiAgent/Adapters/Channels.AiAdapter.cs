using NadekoBot.AiAgent;

namespace NadekoBot.Modules.Utility.AiAgent.Adapters;

public sealed class ChannelsAiAdapter : IAiToolGroup, INService
{
    public string GroupName => "channels";
    public string GroupDescription => "Server channels: list, category grouping, channel metadata.";

    [AiTool("list_channels", "Lists all channels the invoking user can see, grouped by category. Optional category name filter (case-insensitive substring).")]
    public async Task<ChannelListDto> ListChannels(
        AiToolContext ctx,
        [AiParam("Optional category name to filter by (case-insensitive substring match). Pass empty string for all.")] string category = "")
    {
        var allChannels = await ctx.Guild.GetChannelsAsync();

        var categories = new Dictionary<ulong, ICategoryChannel>();
        foreach (var c in allChannels)
        {
            if (c is ICategoryChannel cat)
                categories[cat.Id] = cat;
        }

        var filter = string.IsNullOrWhiteSpace(category) ? null : category.Trim();

        // Use 0 as sentinel for "no category" - real channel IDs are never 0
        var groups = new Dictionary<ulong, ChannelCategoryDto>();

        foreach (var ch in allChannels)
        {
            if (ch is ICategoryChannel)
                continue;

            if (!ctx.User.GetPermissions(ch).ViewChannel)
                continue;

            var categoryId = ch is INestedChannel nc ? nc.CategoryId ?? 0UL : 0UL;
            string categoryName;
            if (categoryId == 0UL)
                categoryName = "(No Category)";
            else if (categories.TryGetValue(categoryId, out var catCh))
                categoryName = catCh.Name;
            else
                categoryName = "Unknown";

            if (filter is not null
                && !categoryName.Contains(filter, StringComparison.InvariantCultureIgnoreCase))
                continue;

            if (!groups.TryGetValue(categoryId, out var group))
            {
                group = new ChannelCategoryDto(categoryId == 0UL ? null : categoryId, categoryName, []);
                groups[categoryId] = group;
            }

            group.Channels.Add(new(ch.Id, ch.Name, GetChannelTypeInternal(ch), ch.Position));
        }

        var ordered = groups.Values
            .OrderBy(static g => g.CategoryId is null ? -1 : int.MaxValue)
            .ThenBy(g =>
            {
                if (g.CategoryId is null || !categories.TryGetValue(g.CategoryId.Value, out var cc))
                    return 0;
                return cc.Position;
            })
            .ToList();

        foreach (var grp in ordered)
            grp.Channels.Sort(static (a, b) => a.Position.CompareTo(b.Position));

        return new(ordered);
    }

    [AiTool("get_channel_info", "Returns detailed info for a single channel: topic, slow mode, NSFW flag, category, type.")]
    public async Task<ChannelInfoDto> GetChannelInfo(
        AiToolContext ctx,
        [AiParam("Channel ID")] ulong channelId)
    {
        var ch = await ctx.Guild.GetChannelAsync(channelId);
        if (ch is null)
            return new(0, null!, null, null, null, null, null, null, "Channel not found.");

        if (!ctx.User.GetPermissions(ch).ViewChannel)
            return new(0, null!, null, null, null, null, null, null, "You do not have permission to view that channel.");

        string? topic = null;
        int? slowMode = null;
        bool? isNsfw = null;
        if (ch is ITextChannel tc)
        {
            topic = tc.Topic;
            slowMode = tc.SlowModeInterval;
            isNsfw = tc.IsNsfw;
        }

        ulong? categoryId = ch is INestedChannel nc ? nc.CategoryId : null;
        string? categoryName = null;
        if (categoryId is not null)
        {
            var cat = await ctx.Guild.GetChannelAsync(categoryId.Value);
            categoryName = cat?.Name;
        }

        return new(
            ch.Id,
            ch.Name,
            GetChannelTypeInternal(ch),
            topic,
            slowMode,
            isNsfw,
            categoryId,
            categoryName,
            null);
    }

    private static string GetChannelTypeInternal(IChannel ch)
        => ch switch
        {
            IStageChannel => "stage",
            IVoiceChannel => "voice",
            IForumChannel => "forum",
            ICategoryChannel => "category",
            ITextChannel => "text",
            _ => "other"
        };
}

public sealed record ChannelListDto(List<ChannelCategoryDto> Categories);

public sealed record ChannelCategoryDto(ulong? CategoryId, string Name, List<ChannelEntryDto> Channels);

public readonly record struct ChannelEntryDto(ulong Id, string Name, string Type, int Position);

public sealed record ChannelInfoDto(
    ulong Id,
    string Name,
    string? Type,
    string? Topic,
    int? SlowModeSeconds,
    bool? IsNsfw,
    ulong? CategoryId,
    string? CategoryName,
    string? Error);
