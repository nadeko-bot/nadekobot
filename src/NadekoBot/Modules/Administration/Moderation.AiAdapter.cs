using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.AiAgent;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Administration.Services;
using NadekoBot.Modules.Utility.AiAgent;

namespace NadekoBot.Modules.Administration;

public sealed class ModerationAiAdapter(
    DbService db,
    ProtectionService protection) : IAiToolGroup, INService
{
    public string GroupName => "moderation";
    public string GroupDescription => "Moderation state: muted users, anti-raid/spam/alt protection configuration.";

    [AiTool("list_muted_users", "Returns the list of currently muted users in this server (from the persistent mute table).")]
    [AiRequiresPerm(GuildPerm.ManageMessages)]
    public async Task<MutedUsersDto> ListMutedUsers(
        AiToolContext ctx,
        [AiParam("Maximum users to return, max 100")] int top = 50)
    {
        top = Math.Clamp(top, 1, 100);
        await using var uow = db.GetDbContext();
        var ids = await uow.GetTable<MutedUserId>()
            .Where(x => x.GuildId == ctx.Guild.Id)
            .OrderBy(x => x.UserId)
            .Take(top)
            .Select(x => x.UserId)
            .ToListAsyncLinqToDB();

        var entries = new List<ulong>(ids.Count);
        foreach (var id in ids)
            entries.Add(id);

        return new(ctx.Guild.Id, entries.Count, entries);
    }

    [AiTool("get_automod_config", "Returns the anti-raid, anti-spam, and anti-alt (automod) configuration currently active in this server.")]
    [AiRequiresPerm(GuildPerm.ManageMessages)]
    public Task<AutomodConfigDto> GetAutomodConfig(AiToolContext ctx)
    {
        var (antiSpam, antiRaid, antiAlt) = protection.GetAntiStats(ctx.Guild.Id);

        AntiRaidDto? raidDto = null;
        if (antiRaid?.AntiRaidSettings is { } rs)
            raidDto = new(rs.UserThreshold, rs.Seconds, rs.Action.ToString(), rs.PunishDuration);

        AntiSpamDto? spamDto = null;
        if (antiSpam?.AntiSpamSettings is { } ss)
            spamDto = new(
                ss.MessageThreshold,
                ss.Action.ToString(),
                ss.MuteTime,
                ss.RoleId,
                ss.IgnoredChannels?.Count ?? 0);

        AntiAltDto? altDto = null;
        if (antiAlt is not null)
            altDto = new(
                antiAlt.MinAge,
                antiAlt.Action.ToString(),
                antiAlt.ActionDurationMinutes,
                antiAlt.RoleId);

        return Task.FromResult(new AutomodConfigDto(ctx.Guild.Id, raidDto, spamDto, altDto));
    }
}

public sealed record MutedUsersDto(ulong GuildId, int Count, List<ulong> UserIds);

public sealed record AutomodConfigDto(
    ulong GuildId,
    AntiRaidDto? AntiRaid,
    AntiSpamDto? AntiSpam,
    AntiAltDto? AntiAlt);

public readonly record struct AntiRaidDto(int UserThreshold, int Seconds, string Action, int PunishDurationMinutes);

public readonly record struct AntiSpamDto(int MessageThreshold, string Action, int MuteTimeMinutes, ulong? RoleId, int IgnoredChannelsCount);

public readonly record struct AntiAltDto(TimeSpan MinAge, string Action, int ActionDurationMinutes, ulong? RoleId);
