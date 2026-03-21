using System.Globalization;
using System.Text.Json;

namespace NadekoBot.Modules.Utility.AiAgent.Tools;

/// <summary>
/// Computes a Unix epoch timestamp from a relative offset or absolute date/time.
/// Returns both the epoch (for Discord timestamp tags) and a human-readable UTC string (for reasoning).
/// Supports parallel calls so the LLM can request multiple timestamps in one turn.
/// </summary>
public sealed class ComputeTimestampTool : IAiTool, INService
{
    public string Name => "compute_timestamp";

    public string Description =>
        "Compute a Unix epoch timestamp for use in Discord timestamp tags like <t:EPOCH:R>. " +
        "Use offset parameters for relative times (e.g. 3 hours from now) or date/time for absolute. " +
        "Returns epoch and human-readable UTC string.";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "offset_seconds": {
                    "type": "integer",
                    "description": "Seconds to add to current time (negative for past)"
                },
                "offset_minutes": {
                    "type": "integer",
                    "description": "Minutes to add to current time (negative for past)"
                },
                "offset_hours": {
                    "type": "integer",
                    "description": "Hours to add to current time (negative for past)"
                },
                "offset_days": {
                    "type": "integer",
                    "description": "Days to add to current time (negative for past)"
                },
                "date": {
                    "type": "string",
                    "description": "Absolute date in yyyy-MM-dd format (uses UTC). Combines with 'time' if provided."
                },
                "time": {
                    "type": "string",
                    "description": "Absolute time in HH:mm or HH:mm:ss format (24h, UTC). Combines with 'date' if provided."
                }
            }
        }
        """).RootElement.Clone();

    public Task<string> ExecuteAsync(AiToolContext context, JsonElement arguments)
    {
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset result;

        var hasDate = arguments.TryGetProperty("date", out var dateEl)
                      && !string.IsNullOrWhiteSpace(dateEl.GetString());
        var hasTime = arguments.TryGetProperty("time", out var timeEl)
                      && !string.IsNullOrWhiteSpace(timeEl.GetString());

        if (hasDate || hasTime)
        {
            var datePart = DateOnly.FromDateTime(now.UtcDateTime);
            var timePart = TimeOnly.MinValue;

            if (hasDate)
            {
                if (!DateOnly.TryParseExact(dateEl.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out datePart))
                    return Task.FromResult("Error: Invalid date format. Use yyyy-MM-dd (e.g. 2026-03-20).");
            }

            if (hasTime)
            {
                var timeStr = timeEl.GetString()!;
                if (!TimeOnly.TryParseExact(timeStr, "HH:mm:ss", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out timePart)
                    && !TimeOnly.TryParseExact(timeStr, "HH:mm", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out timePart))
                    return Task.FromResult("Error: Invalid time format. Use HH:mm or HH:mm:ss (e.g. 15:00 or 15:00:30).");
            }

            result = new DateTimeOffset(datePart.ToDateTime(timePart), TimeSpan.Zero);
        }
        else
        {
            result = now;
        }

        var offset = TimeSpan.Zero;

        if (arguments.TryGetProperty("offset_days", out var daysEl) && daysEl.TryGetInt32(out var days))
            offset += TimeSpan.FromDays(days);

        if (arguments.TryGetProperty("offset_hours", out var hoursEl) && hoursEl.TryGetInt32(out var hours))
            offset += TimeSpan.FromHours(hours);

        if (arguments.TryGetProperty("offset_minutes", out var minutesEl) && minutesEl.TryGetInt32(out var minutes))
            offset += TimeSpan.FromMinutes(minutes);

        if (arguments.TryGetProperty("offset_seconds", out var secondsEl) && secondsEl.TryGetInt32(out var seconds))
            offset += TimeSpan.FromSeconds(seconds);

        result += offset;

        var epoch = result.ToUnixTimeSeconds();
        return Task.FromResult($"epoch: {epoch}\nutc: {result:yyyy-MM-dd HH:mm:ss}");
    }
}
