using NadekoBot.AiAgent;
using NadekoBot.Modules.Utility.AiAgent;
using NadekoBot.Modules.Xp.Services;

namespace NadekoBot.Modules.Xp;

public sealed partial class XpAiAdapter(XpService xp) : IAiToolGroup, INService
{
    public string GroupName => "xp";
    public string GroupDescription => "Server XP, levels, ranks, and leaderboards.";

    [AiTool("get_user_xp", "Returns total XP, current level, and server rank for a user.")]
    public async Task<UserXpDto> GetUserXp(
        AiToolContext ctx,
        [AiParam("Discord user ID")] ulong userId)
    {
        var guildUser = await ctx.Guild.GetUserAsync(userId);
        if (guildUser is null)
            return new(0, 0, 0, "User not found in this server.");

        var stats = await xp.GetUserStatsAsync(guildUser);
        return new(
            stats.FullGuildStats.Xp,
            stats.Guild.Level,
            stats.GuildRanking,
            null);
    }

    [AiTool("get_xp_leaderboard", "Returns the top users by XP in this server.")]
    public async Task<XpLeaderboardDto> GetXpLeaderboard(
        AiToolContext ctx,
        [AiParam("How many users to return, max 25")] int top = 10)
    {
        top = Math.Clamp(top, 1, 25);
        var page = 0;
        var collected = new List<XpLeaderboardEntryDto>();

        while (collected.Count < top)
        {
            var batch = await xp.GetGuildUserXps(ctx.Guild.Id, page);
            if (batch.Count == 0)
                break;

            foreach (var entry in batch)
            {
                if (collected.Count >= top)
                    break;

                var lvl = new NadekoBot.Db.LevelStats(entry.Xp);
                collected.Add(new(entry.UserId, entry.Xp, lvl.Level, collected.Count + 1));
            }

            page++;
        }

        return new(collected);
    }

    [AiTool("get_xp_settings", "Returns XP configuration for this server including level rewards.")]
    public async Task<XpSettingsDto> GetXpSettings(AiToolContext ctx)
    {
        var settings = await xp.GetFullXpSettingsFor(ctx.Guild.Id);
        return new(
            settings.XpFormulaA,
            settings.XpFormulaC,
            settings.CurrencyRewards.Select(static r => new XpCurrencyRewardDto(r.Level, r.Amount)).ToList(),
            settings.RoleRewards.Select(static r => new XpRoleRewardDto(r.Level, r.RoleId, r.Remove)).ToList());
    }
}

public readonly record struct UserXpDto(long TotalXp, long Level, int Rank, string? Error);

public sealed record XpLeaderboardDto(List<XpLeaderboardEntryDto> Entries);

public readonly record struct XpLeaderboardEntryDto(ulong UserId, long Xp, long Level, int Rank);

public sealed record XpSettingsDto(
    int FormulaA,
    int FormulaC,
    List<XpCurrencyRewardDto> CurrencyRewards,
    List<XpRoleRewardDto> RoleRewards);

public readonly record struct XpCurrencyRewardDto(int Level, int Amount);
public readonly record struct XpRoleRewardDto(int Level, ulong RoleId, bool Remove);
