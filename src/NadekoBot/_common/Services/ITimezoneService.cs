namespace NadekoBot.Common;

public interface ITimezoneService
{
    TimeZoneInfo GetTimeZoneOrUtc(ulong? guildId);
}