#nullable disable
namespace NadekoBot.Common.TypeReaders;

public sealed class GuildDateTimeTypeReader : NadekoTypeReader<GuildDateTime>
{
    private readonly ITimezoneService _gts;

    public GuildDateTimeTypeReader(ITimezoneService gts)
        => _gts = gts;

    public override ValueTask<TypeReaderResult<GuildDateTime>> ReadAsync(ICommandContext context, string input)
    {
        var gdt = Parse(context.Guild.Id, input);
        if (gdt is null)
        {
            return new(TypeReaderResult.FromError<GuildDateTime>(CommandError.ParseFailed,
                "Input string is in an incorrect format."));
        }

        return new(TypeReaderResult.FromSuccess(gdt));
    }

    private GuildDateTime Parse(ulong guildId, string input)
    {
        if (!DateTime.TryParse(input, out var dt))
            return null;

        var tz = _gts.GetTimeZoneOrUtc(guildId);

        return new(tz, dt);
    }
}

public class GuildDateTime
{
    public TimeZoneInfo Timezone { get; }
    public DateTime CurrentGuildTime { get; }
    public DateTime InputTime { get; }
    public DateTime InputTimeUtc { get; }

    public GuildDateTime(TimeZoneInfo guildTimezone, DateTime inputTime)
    {
        var now = DateTime.UtcNow;
        Timezone = guildTimezone;
        CurrentGuildTime = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Utc, Timezone);
        InputTime = inputTime;
        InputTimeUtc = TimeZoneInfo.ConvertTime(inputTime, Timezone, TimeZoneInfo.Utc);
    }
}