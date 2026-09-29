using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace NadekoBot.Db;

// sqlite stores no time zone and every stored time is utc, so reads get Kind Utc instead of Unspecified
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    static v => v,
    static v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
