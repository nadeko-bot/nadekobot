using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Administration.Services;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace NadekoBot.Hosted.Endpoints;

public static class HealthEndpoints
{
    private static readonly IResult _notReady = ApiErrors.Create(ApiErrors.NOT_READY, StatusCodes.Status503ServiceUnavailable);

    public static RouteGroupBuilder MapHealthEndpoints(this RouteGroupBuilder guild)
    {
        guild.MapGet("/health", GetHealth);
        return guild;
    }

    private static async Task<IResult> GetHealth(GuildRef guildRef, DbService db, TimeProvider time)
    {
        var guild = guildRef.Guild;
        if (guild.CurrentUser is null)
            return _notReady;

        var sources = await ReadSourcesAsync(db, guild.Id);
        var timers = await ReadTimersAsync(db, guild.Id, time.GetUtcNow().UtcDateTime);

        // The cache can change while the reads run, so the bot user is read again after them.
        if (guild.CurrentUser is not { } bot)
            return _notReady;

        var dto = GuildHealth.Evaluate(new SocketHealthGuild(guild, bot), sources, timers);
        return Results.Json(dto, ApiJsonContext.Default.GuildHealthDto);
    }

    public static async Task<HealthSources> ReadSourcesAsync(DbService db, ulong guildId)
    {
        await using var ctx = db.GetDbContext();

        var greets = await ctx.GetTable<GreetSettings>()
            .Where(x => x.GuildId == guildId && x.IsEnabled)
            .Select(x => new GreetSource(x.GreetType, x.IsEnabled, x.ChannelId, x.MessageText))
            .ToArrayAsyncLinqToDB();

        var xpRewardRoles = await (
                from r in ctx.GetTable<XpRoleReward>()
                join s in ctx.GetTable<XpSettings>() on r.XpSettingsId equals s.Id
                where s.GuildId == guildId
                select r.RoleId)
            .ToArrayAsyncLinqToDB();

        var notifyChannels = await ctx.GetTable<Notify>()
            .Where(x => x.GuildId == guildId
                        && (x.Type == NotifyType.LevelUp
                            || x.Type == NotifyType.AddRoleReward
                            || x.Type == NotifyType.RemoveRoleReward))
            .Select(x => x.ChannelId)
            .ToArrayAsyncLinqToDB();

        var punishments = await ctx.GetTable<WarningPunishment>()
            .Where(x => x.GuildId == guildId)
            .Select(x => new PunishSource(x.Punishment, x.RoleId))
            .ToArrayAsyncLinqToDB();

        var config = await ctx.GetTable<GuildConfig>()
            .Where(x => x.GuildId == guildId)
            .Select(x => new { x.AutoAssignRoleIds, x.MuteRoleId })
            .FirstOrDefaultAsyncLinqToDB();

        var selfAssignRoles = await ctx.GetTable<Sar>()
            .Where(x => x.GuildId == guildId)
            .Select(x => x.RoleId)
            .ToArrayAsyncLinqToDB();

        var reactionRoles = await ctx.GetTable<ReactionRoleV2>()
            .Where(x => x.GuildId == guildId)
            .Select(x => new ChannelRoleSource(x.ChannelId, x.RoleId))
            .ToArrayAsyncLinqToDB();

        var buttonRoles = await ctx.GetTable<ButtonRole>()
            .Where(x => x.GuildId == guildId)
            .Select(x => new ChannelRoleSource(x.ChannelId, x.RoleId))
            .ToArrayAsyncLinqToDB();

        var logChannels = await ctx.GetTable<LogChannel>()
            .Where(x => x.GuildId == guildId)
            .Select(x => x.ChannelId)
            .ToArrayAsyncLinqToDB();

        var streamChannels = await ctx.GetTable<FollowedStream>()
            .Where(x => x.GuildId == guildId)
            .Select(x => x.ChannelId)
            .ToArrayAsyncLinqToDB();

        var repeaters = await ctx.GetTable<Repeater>()
            .Where(x => x.GuildId == guildId)
            .Select(x => new RepeaterSource(x.ChannelId, x.Message))
            .ToArrayAsyncLinqToDB();

        return new HealthSources(
            greets,
            xpRewardRoles,
            notifyChannels,
            punishments,
            Modules.Administration.Services.GuildConfigExtensions.ParseRoleIds(config?.AutoAssignRoleIds),
            config?.MuteRoleId,
            selfAssignRoles,
            reactionRoles,
            buttonRoles,
            logChannels,
            streamChannels,
            repeaters);
    }

    // A timer which is already due waits only for the next check, so it does not count.
    public static async Task<HealthTimersDto> ReadTimersAsync(DbService db, ulong guildId, DateTime now)
    {
        await using var ctx = db.GetDbContext();

        var mutes = ctx.GetTable<UnmuteTimer>().Where(x => x.GuildId == guildId && x.UnmuteAt > now);
        var bans = ctx.GetTable<UnbanTimer>().Where(x => x.GuildId == guildId && x.UnbanAt > now);
        var tempRoles = ctx.GetTable<TempRole>().Where(x => x.GuildId == guildId && x.ExpiresAt > now);

        var muteCount = await mutes.CountAsyncLinqToDB();
        var banCount = await bans.CountAsyncLinqToDB();
        var tempRoleCount = await tempRoles.CountAsyncLinqToDB();

        DateTime? next = null;
        if (muteCount > 0)
            next = Earlier(next, await mutes.MinAsyncLinqToDB(static x => (DateTime?)x.UnmuteAt));
        if (banCount > 0)
            next = Earlier(next, await bans.MinAsyncLinqToDB(static x => (DateTime?)x.UnbanAt));
        if (tempRoleCount > 0)
            next = Earlier(next, await tempRoles.MinAsyncLinqToDB(static x => (DateTime?)x.ExpiresAt));

        return new HealthTimersDto(muteCount,
            banCount,
            tempRoleCount,
            next is { } n ? DateTime.SpecifyKind(n, DateTimeKind.Utc) : null);
    }

    private static DateTime? Earlier(DateTime? a, DateTime? b)
        => a is null || (b is not null && b < a) ? b : a;
}
