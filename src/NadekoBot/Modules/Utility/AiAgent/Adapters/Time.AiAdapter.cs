using System.Globalization;
using NadekoBot.AiAgent;

namespace NadekoBot.Modules.Utility.AiAgent.Adapters;

public sealed class TimeAiAdapter : IAiCoreToolGroup, INService
{
    public string GroupName => "time";
    public string GroupDescription => "Compute timestamps for use in Discord timestamp tags.";

    [AiTool(
        "compute_timestamp",
        "Compute a Unix epoch timestamp for use in Discord timestamp tags like <t:EPOCH:R>. "
        + "Use offset parameters for relative times (e.g. 3 hours from now) or date/time for absolute. "
        + "Returns epoch and human-readable UTC string.")]
    public Task<string> ComputeTimestamp(
        [AiParam("Seconds to add to current time (negative for past)")]
        int offsetSeconds = 0,
        [AiParam("Minutes to add to current time (negative for past)")]
        int offsetMinutes = 0,
        [AiParam("Hours to add to current time (negative for past)")]
        int offsetHours = 0,
        [AiParam("Days to add to current time (negative for past)")]
        int offsetDays = 0,
        [AiParam("Absolute date in yyyy-MM-dd format (uses UTC). Combines with 'time' if provided.")]
        string? date = null,
        [AiParam("Absolute time in HH:mm or HH:mm:ss format (24h, UTC). Combines with 'date' if provided.")]
        string? time = null)
    {
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset result;

        var hasDate = !string.IsNullOrWhiteSpace(date);
        var hasTime = !string.IsNullOrWhiteSpace(time);

        if (hasDate || hasTime)
        {
            var datePart = DateOnly.FromDateTime(now.UtcDateTime);
            var timePart = TimeOnly.MinValue;

            if (hasDate
                && !DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out datePart))
                throw ToolException.InvalidArgument("Invalid date format. Use yyyy-MM-dd (e.g. 2026-03-20).");

            if (hasTime
                && !TimeOnly.TryParseExact(time, "HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out timePart)
                && !TimeOnly.TryParseExact(time, "HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out timePart))
                throw ToolException.InvalidArgument("Invalid time format. Use HH:mm or HH:mm:ss (e.g. 15:00 or 15:00:30).");

            result = new DateTimeOffset(datePart.ToDateTime(timePart), TimeSpan.Zero);
        }
        else
        {
            result = now;
        }

        result += TimeSpan.FromDays(offsetDays)
                  + TimeSpan.FromHours(offsetHours)
                  + TimeSpan.FromMinutes(offsetMinutes)
                  + TimeSpan.FromSeconds(offsetSeconds);

        var epoch = result.ToUnixTimeSeconds();
        return Task.FromResult($"epoch: {epoch}\nutc: {result:yyyy-MM-dd HH:mm:ss}");
    }
}
