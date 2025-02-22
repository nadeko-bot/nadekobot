namespace NadekoBot.Modules.Patronage;

public static class PatronExtensions
{
    public static string ToFullName(this PatronTier tier)
        => tier switch
        {
            _ => $"Patron Tier {tier}",
        };

    public static DateTime DayOfNextMonth(this DateTime date, int day)
    {
        var nextMonth = date.AddMonths(1);
        var dt = DateTime.SpecifyKind(new(nextMonth.Year, nextMonth.Month, day), DateTimeKind.Utc);
        return dt;
    }

    public static DateTime FirstOfNextMonth(this DateTime date)
        => date.DayOfNextMonth(1);

    public static DateTime SecondOfNextMonth(this DateTime date)
        => date.DayOfNextMonth(2);

    public static string ToShortAndRelativeTimestampTag(this DateTime date)
    {
        var fullResetStr = TimestampTag.FromDateTime(date, TimestampTagStyles.ShortDateTime);
        var relativeResetStr = TimestampTag.FromDateTime(date, TimestampTagStyles.Relative);
        return $"{fullResetStr}\n{relativeResetStr}";
    }
}