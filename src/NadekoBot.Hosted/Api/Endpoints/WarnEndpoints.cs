using System.Collections.Frozen;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Administration.Services;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace NadekoBot.Hosted.Endpoints;

public static class WarnEndpoints
{
    private const string API_MODERATOR = "api";

    private static readonly FrozenDictionary<PunishmentAction, string> _punishmentNames = BuildPunishmentNamesInternal();

    // Enum.TryParse would also take numbers and comma lists, which combine into a different punishment.
    private static readonly FrozenDictionary<string, PunishmentAction> _punishmentsByName = BuildPunishmentsByNameInternal();

    public static RouteGroupBuilder MapWarnEndpoints(this RouteGroupBuilder guild)
    {
        guild.MapGet("/warnings", GetWarnings);
        guild.MapPost("/warnings/{id:int}/forgive", ForgiveWarning);
        guild.MapDelete("/warnings/{id:int}", DeleteWarning);

        guild.MapGet("/warn-punishments", GetWarnPunishments);
        guild.MapPut("/warn-punishments/{count:int}", SetWarnPunishment);
        guild.MapDelete("/warn-punishments/{count:int}", DeleteWarnPunishment);

        guild.MapGet("/warn-expiry", GetWarnExpiry);
        guild.MapPut("/warn-expiry", SetWarnExpiry);
        return guild;
    }

    private static async Task<IResult> GetWarnings(GuildRef guild, int? page, string? userId, UserPunishService svc)
    {
        ulong uid = 0;
        if (userId is not null && !ApiLimits.TryParseId(userId, out uid))
            return ApiErrors.InvalidValue;

        var pageNum = ApiLimits.Page(page, 1);
        var (warnings, total) = userId is not null
            ? await svc.GetUserWarnings(guild.Id, uid, pageNum)
            : await svc.GetLatestWarnings(guild.Id, pageNum);

        var items = new WarningDto[warnings.Count];
        var i = 0;
        foreach (var w in warnings)
            items[i++] = ToWarningDto(w);

        return Results.Json(new WarningListDto(total, items), ApiJsonContext.Default.WarningListDto);
    }

    private static async Task<IResult> ForgiveWarning(
        GuildRef guild,
        int id,
        ForgiveWarningRequest? body,
        UserPunishService svc)
    {
        var moderator = body?.Moderator;
        if (moderator is not null && !ApiLimits.IsText(moderator, ApiLimits.MAX_NAME_LENGTH))
            return ApiErrors.InvalidValue;

        return await svc.ForgiveWarning(guild.Id, id, moderator ?? API_MODERATOR)
            ? Results.NoContent()
            : ApiErrors.NotFound;
    }

    private static async Task<IResult> DeleteWarning(GuildRef guild, int id, UserPunishService svc)
        => await svc.WarnDelete(guild.Id, id) ? Results.NoContent() : ApiErrors.NotFound;

    private static async Task<IResult> GetWarnPunishments(GuildRef guild, UserPunishService svc)
    {
        var ps = await svc.WarnPunishList(guild.Id);
        var arr = new WarnPunishmentDto[ps.Length];
        for (var i = 0; i < ps.Length; i++)
            arr[i] = ToWarnPunishmentDto(ps[i]);

        return Results.Json(arr, ApiJsonContext.Default.WarnPunishmentDtoArray);
    }

    private static async Task<IResult> SetWarnPunishment(
        GuildRef guildRef,
        int count,
        SetWarnPunishmentRequest body,
        UserPunishService svc)
    {
        if (!TryParsePunishment(body.Punishment, out var punishment))
            return ApiErrors.InvalidValue;

        if (body.TimeMinutes is < 0)
            return ApiErrors.InvalidValue;

        TimeSpan? time = body.TimeMinutes is { } m and > 0 ? TimeSpan.FromMinutes(m) : null;

        IRole? role = null;
        if (punishment == PunishmentAction.AddRole)
        {
            if (!ApiLimits.TryParseId(body.RoleId, out var roleId) || guildRef.Guild.GetRole(roleId) is not { } found)
                return ApiErrors.RoleNotFound;
            if (!RoleChecks.CanBotAssign(guildRef.Guild, found))
                return ApiErrors.MissingPermissions;
            role = found;
        }

        // The service checks count, time and type together, as for .warnpunish.
        if (!await svc.WarnPunish(guildRef.Id, count, punishment, time, role))
            return ApiErrors.InvalidValue;

        var updated = await svc.WarnPunishList(guildRef.Id);
        foreach (var p in updated)
        {
            if (p.Count == count)
                return Results.Json(ToWarnPunishmentDto(p), ApiJsonContext.Default.WarnPunishmentDto);
        }

        return ApiErrors.NotFound;
    }

    private static async Task<IResult> DeleteWarnPunishment(GuildRef guild, int count, UserPunishService svc)
        => await svc.WarnPunishRemove(guild.Id, count) ? Results.NoContent() : ApiErrors.NotFound;

    private static async Task<IResult> GetWarnExpiry(GuildRef guild, UserPunishService svc)
    {
        var (days, delete) = await svc.GetWarnExpire(guild.Id);
        return Results.Json(new WarnExpiryDto(days, delete), ApiJsonContext.Default.WarnExpiryDto);
    }

    private static async Task<IResult> SetWarnExpiry(GuildRef guild, SetWarnExpiryRequest body, UserPunishService svc)
    {
        if (!IsValidExpiryDays(body.Days))
            return ApiErrors.InvalidValue;

        await svc.WarnExpireAsync(guild.Id, body.Days, body.Delete);

        var (days, delete) = await svc.GetWarnExpire(guild.Id);
        return Results.Json(new WarnExpiryDto(days, delete), ApiJsonContext.Default.WarnExpiryDto);
    }

    public static bool IsValidExpiryDays(int days)
        => days is >= 0 and <= ApiLimits.MAX_WARN_EXPIRE_DAYS;

    public static bool TryParsePunishment(string? input, out PunishmentAction punishment)
    {
        if (input is not null && _punishmentsByName.TryGetValue(input, out punishment))
            return true;

        punishment = default;
        return false;
    }

    public static WarningDto ToWarningDto(Warning w)
        => new(
            w.Id,
            w.UserId.ToString(),
            w.Reason ?? string.Empty,
            w.Forgiven,
            w.ForgivenBy,
            w.Moderator ?? string.Empty,
            w.Weight,
            w.DateAdded is { } added ? DateTime.SpecifyKind(added, DateTimeKind.Utc) : null);

    public static WarnPunishmentDto ToWarnPunishmentDto(WarningPunishment p)
        => new(
            p.Count,
            _punishmentNames.TryGetValue(p.Punishment, out var name) ? name : p.Punishment.ToString(),
            p.Time,
            p.RoleId?.ToString());

    private static FrozenDictionary<PunishmentAction, string> BuildPunishmentNamesInternal()
    {
        var values = Enum.GetValues<PunishmentAction>();
        var names = new Dictionary<PunishmentAction, string>(values.Length);
        foreach (var v in values)
            names[v] = v.ToString().ToLowerInvariant();

        return names.ToFrozenDictionary();
    }

    private static FrozenDictionary<string, PunishmentAction> BuildPunishmentsByNameInternal()
    {
        var values = Enum.GetValues<PunishmentAction>();
        var byName = new Dictionary<string, PunishmentAction>(values.Length, StringComparer.InvariantCultureIgnoreCase);
        foreach (var v in values)
        {
            // A warn punishment would warn again and so skip steps of the ladder.
            if (v != PunishmentAction.Warn)
                byName[v.ToString()] = v;
        }

        return byName.ToFrozenDictionary(StringComparer.InvariantCultureIgnoreCase);
    }
}
