using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NadekoBot.Db.Models;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace NadekoBot.Hosted.Endpoints;

public static class ActivityEndpoints
{
    public const int DEFAULT_DAYS = 7;

    private static readonly IResult _notReady = ApiErrors.Create(ApiErrors.NOT_READY, StatusCodes.Status503ServiceUnavailable);

    public static RouteGroupBuilder MapActivityEndpoints(this RouteGroupBuilder ownerGuild)
    {
        ownerGuild.MapGet("/activity", GetActivity);
        return ownerGuild;
    }

    // Only the two periods which the dashboard shows; a bound int would also take "+7" or " 7".
    public static int? ParseDays(string? days)
        => days switch
        {
            null => DEFAULT_DAYS,
            "7" => DEFAULT_DAYS,
            "30" => GuildActivityService.RETENTION_DAYS,
            _ => null
        };

    private static async Task<IResult> GetActivity(
        GuildRef guild,
        string? days,
        GuildActivityService activity,
        DbService db,
        HttpContext http)
    {
        if (ParseDays(days) is not { } dayCount)
            return ApiErrors.InvalidValue;

        var report = await activity.ReadAsync(guild.Id, dayCount, http.RequestAborted);
        if (report is null)
            return _notReady;

        var warnings = await CountWarningsPerDayAsync(db, guild.Id, report.FirstDay, dayCount);
        return Results.Json(ToDto(report, warnings), ApiJsonContext.Default.GuildActivityDto);
    }

    public static GuildActivityDto ToDto(ActivityReport report, long[] warnings)
    {
        var days = new ActivityDayDto[report.Days.Length];
        for (var i = 0; i < days.Length; i++)
        {
            ref readonly var c = ref report.Days[i];
            days[i] = new ActivityDayDto(DateOnly.FromDateTime(DayStart(report.FirstDay + i)),
                c.Commands,
                c.Errors,
                c.Joins,
                c.Leaves,
                warnings[i]);
        }

        DateTime? since = report.FirstHour is { } hour
            ? DayStart(hour / ActivityReportBuilder.HOURS_PER_DAY)
            : null;

        return new GuildActivityDto(since, days, report.TopCommands, report.Hours);
    }

    public static async Task<long[]> CountWarningsPerDayAsync(DbService db, ulong guildId, long firstDay, int dayCount)
    {
        var from = DayStart(firstDay);
        var to = from.AddDays(dayCount);

        await using var ctx = db.GetDbContext();
        var rows = await ctx.GetTable<Warning>()
            .Where(x => x.GuildId == guildId && x.DateAdded >= from && x.DateAdded < to)
            .GroupBy(x => x.DateAdded!.Value.Date)
            .Select(g => new { Day = g.Key, Count = g.LongCount() })
            .ToArrayAsyncLinqToDB();

        var counts = new long[dayCount];
        foreach (var row in rows)
        {
            var index = (row.Day - from).Days;
            if ((uint)index < (uint)dayCount)
                counts[index] += row.Count;
        }

        return counts;
    }

    private static DateTime DayStart(long unixDay)
        => DateTime.UnixEpoch.AddDays(unixDay);
}
