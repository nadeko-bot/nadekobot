using NadekoBot.AiAgent;
using NadekoBot.Modules.Administration.Services;
using NadekoBot.Modules.Utility.AiAgent;

namespace NadekoBot.Modules.Administration.UserPunish;

public sealed class WarningsAiAdapter(UserPunishService ups) : IAiToolGroup, INService
{
    public string GroupName => "warnings";
    public string GroupDescription => "Moderation warnings: list, count, and punishment configuration.";

    [AiTool("list_user_warnings", "Returns the most recent warnings for a user in this server.")]
    [AiRequiresPerm(GuildPerm.KickMembers)]
    public async Task<UserWarningsDto> ListUserWarnings(
        AiToolContext ctx,
        [AiParam("Discord user ID")] ulong userId,
        [AiParam("Page of warnings to fetch, 1 = most recent")] int page = 1)
    {
        page = Math.Max(1, page);
        var (latest, total) = await ups.GetUserWarnings(ctx.Guild.Id, userId, page);

        var entries = new List<UserWarningDto>(latest.Count);
        foreach (var w in latest)
        {
            entries.Add(new(
                w.Id,
                w.Reason,
                w.Moderator,
                w.Forgiven,
                w.ForgivenBy,
                w.Weight,
                w.DateAdded));
        }

        return new(userId, total, page, entries);
    }

    [AiTool("count_user_warnings", "Returns the total weighted warning count for a user in this server.")]
    [AiRequiresPerm(GuildPerm.KickMembers)]
    public async Task<UserWarningCountDto> CountUserWarnings(
        AiToolContext ctx,
        [AiParam("Discord user ID")] ulong userId)
    {
        var count = await ups.GetCurrentWarnCount(ctx.Guild.Id, userId);
        return new(userId, count);
    }

    [AiTool("get_warning_punishments", "Returns the configured automatic punishments for warning thresholds in this server.")]
    public async Task<WarningPunishmentsDto> GetWarningPunishments(AiToolContext ctx)
    {
        var list = await ups.WarnPunishList(ctx.Guild.Id);
        var entries = new List<WarningPunishmentEntryDto>(list.Length);
        foreach (var p in list)
        {
            entries.Add(new(
                p.Count,
                p.Punishment.ToString(),
                p.Time,
                p.RoleId));
        }

        var (expireDays, deleteOnExpire) = await ups.GetWarnExpire(ctx.Guild.Id);

        return new(entries, expireDays, deleteOnExpire);
    }
}

public sealed record UserWarningsDto(ulong UserId, int TotalCount, int Page, List<UserWarningDto> Warnings);

public readonly record struct UserWarningDto(
    int Id,
    string? Reason,
    string? Moderator,
    bool Forgiven,
    string? ForgivenBy,
    long Weight,
    DateTime? DateAdded);

public readonly record struct UserWarningCountDto(ulong UserId, long WeightedCount);

public sealed record WarningPunishmentsDto(
    List<WarningPunishmentEntryDto> Punishments,
    int ExpireDays,
    bool DeleteOnExpire);

public readonly record struct WarningPunishmentEntryDto(int Count, string Action, int TimeMinutes, ulong? RoleId);
