using NadekoBot.AiAgent;
using NadekoBot.Modules.Utility.AiAgent;

namespace NadekoBot.Modules.Waifus.Waifu;

public sealed class WaifuAiAdapter(WaifuService waifu) : IAiToolGroup, INService
{
    public string GroupName => "waifu";
    public string GroupDescription => "Waifu system: user waifu profiles, fans, leaderboards.";

    [AiTool("get_waifu_info", "Returns waifu profile for a user: price, mood, food, total backed, fan count, manager.")]
    public async Task<WaifuInfoResultDto> GetWaifuInfo(
        AiToolContext ctx,
        [AiParam("Discord user ID")] ulong userId)
    {
        var info = await waifu.GetWaifuInfoAsync(userId);
        if (info is null)
            return new(false, null, "User has not opted in as a waifu.");

        return new(true, new WaifuProfileDto(
            info.UserId,
            info.Price,
            info.Mood,
            info.Food,
            info.TotalProduced,
            info.ReturnsCap,
            info.FanCount,
            info.SnapshotTotalBacked,
            info.ManagerId,
            info.IsHubby,
            info.Description,
            info.Quote), null);
    }

    [AiTool("get_waifu_leaderboard", "Returns the top waifus ordered by total backed amount (default) or price.")]
    public async Task<WaifuLeaderboardDto> GetWaifuLeaderboard(
        AiToolContext ctx,
        [AiParam("Sort order: 'backing' (default) or 'price'")] string order = "backing",
        [AiParam("How many waifus to return, max 25")] int top = 10)
    {
        top = Math.Clamp(top, 1, 25);
        var lbOrder = string.Equals(order, "price", StringComparison.InvariantCultureIgnoreCase)
            ? WaifuLbOrder.Price
            : WaifuLbOrder.Backing;

        var collected = new List<WaifuLeaderboardEntryDto>(top);
        var page = 0;
        const int pageSize = 9;

        while (collected.Count < top)
        {
            var batch = await waifu.GetLeaderboardAsync(lbOrder, page, pageSize);
            if (batch.Count == 0)
                break;

            foreach (var entry in batch)
            {
                if (collected.Count >= top)
                    break;

                collected.Add(new(
                    entry.UserId,
                    entry.Username,
                    entry.Price,
                    entry.SnapshotTotalBacked,
                    entry.Mood,
                    entry.Food,
                    entry.HasManager,
                    collected.Count + 1));
            }

            if (batch.Count < pageSize)
                break;

            page++;
        }

        return new(lbOrder.ToString(), collected);
    }

    [AiTool("list_user_waifus", "Returns the waifus a user manages (owns as a manager).")]
    public async Task<ManagedWaifuListDto> ListUserWaifus(
        AiToolContext ctx,
        [AiParam("Discord user ID of the manager")] ulong userId)
    {
        var managed = await waifu.GetManagedWaifusAsync(userId);
        var entries = new List<ManagedWaifuEntryDto>(managed.Count);
        foreach (var w in managed)
            entries.Add(new(w.WaifuUserId, w.Username, w.Price, w.TotalBacked));

        return new(userId, entries);
    }
}

public sealed record WaifuInfoResultDto(bool Found, WaifuProfileDto? Profile, string? Error);

public sealed record WaifuProfileDto(
    ulong UserId,
    long Price,
    int Mood,
    int Food,
    long TotalProduced,
    long ReturnsCap,
    int FanCount,
    long TotalBacked,
    ulong? ManagerId,
    bool IsHubby,
    string? Description,
    string? Quote);

public sealed record WaifuLeaderboardDto(string Order, List<WaifuLeaderboardEntryDto> Entries);

public readonly record struct WaifuLeaderboardEntryDto(
    ulong UserId,
    string? Username,
    long Price,
    long TotalBacked,
    int Mood,
    int Food,
    bool HasManager,
    int Rank);

public sealed record ManagedWaifuListDto(ulong ManagerUserId, List<ManagedWaifuEntryDto> Waifus);

public readonly record struct ManagedWaifuEntryDto(ulong UserId, string? Username, long Price, long TotalBacked);
