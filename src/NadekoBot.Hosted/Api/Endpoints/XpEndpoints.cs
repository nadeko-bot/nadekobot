using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Xp;
using NadekoBot.Modules.Xp.Services;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace NadekoBot.Hosted.Endpoints;

public static class XpEndpoints
{
    // An api call has no channel; level-up notices then go only to the server's configured notify channel.
    private const ulong NO_ORIGIN_CHANNEL = 0;

    // XpService.GetGuildUserXps returns pages of this size.
    public const int PAGE_SIZE = 10;

    public static RouteGroupBuilder MapXpEndpoints(this RouteGroupBuilder guild)
    {
        guild.MapGet("/xp/leaderboard", GetLeaderboard);
        guild.MapGet("/xp/rewards", GetRewards);
        guild.MapPut("/xp/rewards/roles/{level:int}", SetRoleReward);
        guild.MapDelete("/xp/rewards/roles/{level:int}", DeleteRoleReward);
        guild.MapPost("/xp/users/{userId}/add", AddUserXp).RequireRateLimiting(ApiHost.DISCORD_CALL_POLICY);
        guild.MapPut("/xp/users/{userId}/level", SetUserLevel);
        guild.MapDelete("/xp/users/{userId}", ResetUserXp);
        guild.MapDelete("/xp", ResetGuildXp);
        return guild;
    }

    // A currency reward creates currency, so only the owner's key may set it. Coordinated mode does not map these routes.
    public static RouteGroupBuilder MapXpCurrencyRewardEndpoints(this RouteGroupBuilder ownerGuild)
    {
        ownerGuild.MapPut("/xp/rewards/currency/{level:int}", SetCurrencyReward);
        ownerGuild.MapDelete("/xp/rewards/currency/{level:int}", DeleteCurrencyReward);
        return ownerGuild;
    }

    private static async Task<IResult> GetLeaderboard(
        GuildRef guildRef,
        int? page,
        XpService svc,
        XpFormulaService formulaSvc,
        DbService db)
    {
        var guild = guildRef.Guild;
        var pageIndex = ApiLimits.Page(page, 0);
        var users = await svc.GetGuildUserXps(guild.Id, pageIndex);
        var total = await svc.GetGuildXpUsersCountAsync(guild.Id);
        var formula = formulaSvc.GetFormula(guild.Id);

        var names = new string?[users.Count];
        var uncached = new List<ulong>(users.Count);
        var i = 0;
        foreach (var u in users)
        {
            if ((names[i++] = guild.GetUser(u.UserId)?.Username) is null)
                uncached.Add(u.UserId);
        }

        var stored = await ReadStoredUsernamesAsync(db, uncached);

        var items = new XpUserDto[users.Count];
        i = 0;
        foreach (var u in users)
        {
            var rank = (pageIndex * PAGE_SIZE) + i + 1;
            var name = names[i] ?? stored.GetValueOrDefault(u.UserId);
            items[i++] = ToXpUserDto(u, formula, rank, name);
        }

        return Results.Json(new XpLeaderboardDto(total, items), ApiJsonContext.Default.XpLeaderboardDto);
    }

    // Members who left or are not cached on large servers still have the name the bot stored last.
    public static async Task<Dictionary<ulong, string?>> ReadStoredUsernamesAsync(DbService db, List<ulong> userIds)
    {
        if (userIds.Count == 0)
            return [];

        await using var ctx = db.GetDbContext();
        return await ctx.GetTable<DiscordUser>()
                        .Where(x => userIds.Contains(x.UserId))
                        .Select(x => new { x.UserId, x.Username })
                        .ToDictionaryAsyncLinqToDB(static x => x.UserId, static x => x.Username);
    }

    private static async Task<IResult> GetRewards(GuildRef guild, DbService db)
        => Results.Json(await ReadRewardsAsync(db, guild.Id), ApiJsonContext.Default.XpRewardsDto);

    // XpService.GetFullXpSettingsFor inserts a missing row, and two first reads at once would hit its unique index.
    public static async Task<XpRewardsDto> ReadRewardsAsync(DbService db, ulong guildId)
    {
        await using var ctx = db.GetDbContext();
        var settings = await ctx.GetTable<XpSettings>()
                                .Where(x => x.GuildId == guildId)
                                .LoadWith(x => x.RoleRewards)
                                .LoadWith(x => x.CurrencyRewards)
                                .FirstOrDefaultAsyncLinqToDB();
        if (settings is null)
            return new XpRewardsDto([], []);

        var roleRewards = settings.RoleRewards;
        var roles = new XpRoleRewardDto[roleRewards.Count];
        var roleLevels = new int[roles.Length];
        var i = 0;
        foreach (var r in roleRewards)
        {
            roleLevels[i] = r.Level;
            roles[i++] = new XpRoleRewardDto(r.Level, r.RoleId.ToString(), r.Remove);
        }

        Array.Sort(roleLevels, roles);

        var currencyRewards = settings.CurrencyRewards;
        var currency = new XpCurrencyRewardDto[currencyRewards.Count];
        var currencyLevels = new int[currency.Length];
        i = 0;
        foreach (var c in currencyRewards)
        {
            currencyLevels[i] = c.Level;
            currency[i++] = new XpCurrencyRewardDto(c.Level, c.Amount);
        }

        Array.Sort(currencyLevels, currency);

        return new XpRewardsDto(roles, currency);
    }

    private static async Task<IResult> SetRoleReward(
        GuildRef guild,
        int level,
        SetXpRoleRewardRequest body,
        XpService svc)
    {
        if (!IsRewardLevel(level))
            return ApiErrors.InvalidValue;

        if (!ApiLimits.TryParseId(body.RoleId, out var roleId) || guild.Guild.GetRole(roleId) is not { } role)
            return ApiErrors.RoleNotFound;

        if (!RoleChecks.CanBotAssign(guild.Guild, role))
            return ApiErrors.MissingPermissions;

        if (!await svc.SetRoleRewardAsync(guild.Id, level, roleId, body.Remove ?? false))
            return ApiErrors.LimitReached;

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteRoleReward(GuildRef guild, int level, XpService svc)
    {
        await svc.ResetRoleRewardAsync(guild.Id, level);
        return Results.NoContent();
    }

    private static async Task<IResult> SetCurrencyReward(
        GuildRef guild,
        int level,
        SetXpCurrencyRewardRequest body,
        XpService svc)
    {
        if (!IsRewardLevel(level) || body.Amount <= 0)
            return ApiErrors.InvalidValue;

        await svc.SetCurrencyReward(guild.Id, level, body.Amount);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteCurrencyReward(GuildRef guild, int level, XpService svc)
    {
        await svc.SetCurrencyReward(guild.Id, level, 0);
        return Results.NoContent();
    }

    // Each grant can level the member up, and a level up sends a message and changes roles.
    private static async Task<IResult> AddUserXp(GuildRef guildRef, string userId, AddXpRequest body, XpService svc)
    {
        if (!TryParseUserIdInternal(userId, out var id)
            || body.Amount is 0 or < -ApiLimits.MAX_XP_AMOUNT or > ApiLimits.MAX_XP_AMOUNT)
            return ApiErrors.InvalidValue;

        var guild = guildRef.Guild;
        if (guild.GetUser(id) is not { } user)
            return ApiErrors.UserNotFound;

        await svc.AddXpAsync(NO_ORIGIN_CHANNEL, body.Amount, user);
        return Results.NoContent();
    }

    private static async Task<IResult> SetUserLevel(GuildRef guild, string userId, SetXpLevelRequest body, XpService svc)
    {
        if (!TryParseUserIdInternal(userId, out var id) || body.Level is < 0 or > ApiLimits.MAX_XP_LEVEL)
            return ApiErrors.InvalidValue;

        await svc.SetLevelAsync(guild.Id, id, body.Level);
        return Results.NoContent();
    }

    private static async Task<IResult> ResetUserXp(GuildRef guild, string userId, XpService svc)
    {
        if (!TryParseUserIdInternal(userId, out var id))
            return ApiErrors.InvalidValue;

        await svc.XpReset(guild.Id, id);
        return Results.NoContent();
    }

    // Setting a level creates the row, so a typo must not create one for user 0.
    private static bool TryParseUserIdInternal(string text, out ulong id)
        => ApiLimits.TryParseId(text, out id) && id != 0;

    // XpService.XpReset(guildId) deletes the same rows, but synchronously on the request thread.
    private static async Task<IResult> ResetGuildXp(GuildRef guild, DbService db)
    {
        await using var ctx = db.GetDbContext();
        await ctx.GetTable<UserXpStats>()
                 .Where(x => x.GuildId == guild.Id)
                 .DeleteAsync();
        return Results.NoContent();
    }

    public static bool IsRewardLevel(int level)
        => level is >= ApiLimits.MIN_REWARD_LEVEL and <= ApiLimits.MAX_XP_LEVEL;

    public static XpUserDto ToXpUserDto(UserXpStats stats, XpFormula formula, int rank, string? username)
    {
        var levelStats = new LevelStats(stats.Xp, formula.A, formula.C);
        return new XpUserDto(
            rank,
            stats.UserId.ToString(),
            username,
            levelStats.Level,
            levelStats.LevelXp,
            levelStats.RequiredXp,
            levelStats.TotalXp);
    }
}
