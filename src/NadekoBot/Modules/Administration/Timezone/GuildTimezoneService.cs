#nullable disable
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Db.Models;
using NadekoBot.Common.ModuleBehaviors;

namespace NadekoBot.Modules.Administration.Services;

public sealed class GuildTimezoneService : ITimezoneService, IReadyExecutor, INService
{
    private readonly ConcurrentDictionary<ulong, TimeZoneInfo> _timezones = new();
    private readonly DbService _db;
    private readonly IReplacementPatternStore _repStore;
    private readonly ShardData _shardData;
    private readonly DiscordSocketClient _client;

    public GuildTimezoneService(
        DbService db,
        IReplacementPatternStore repStore,
        ShardData shardData,
        DiscordSocketClient client)
    {
        _db = db;
        _repStore = repStore;
        _shardData = shardData;
        _client = client;
    }

    private static TimeZoneInfo TryGetById(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch
        {
            return null;
        }
    }

    // zone ids are file names on linux, so the exact lookup is case sensitive
    public static TimeZoneInfo TryFindTimeZone(string input)
    {
        var query = input.AsSpan().Trim();
        if (query.IsEmpty)
            return null;

        if (TryGetById(query.ToString()) is { } exact)
            return exact;

        TimeZoneInfo cityMatch = null;
        var cityMatches = 0;
        foreach (var tz in TimeZoneInfo.GetSystemTimeZones())
        {
            var id = tz.Id.AsSpan();
            if (id.Equals(query, StringComparison.InvariantCultureIgnoreCase))
                return tz;

            var city = id[(id.LastIndexOf('/') + 1)..];
            if (city.Length == query.Length && CityEquals(city, query))
            {
                cityMatch = tz;
                cityMatches++;
            }
        }

        return cityMatches == 1 ? cityMatch : null;
    }

    private static bool CityEquals(ReadOnlySpan<char> city, ReadOnlySpan<char> query)
    {
        for (var i = 0; i < city.Length; i++)
        {
            var q = query[i] == ' ' ? '_' : query[i];
            if (char.ToUpperInvariant(city[i]) != char.ToUpperInvariant(q))
                return false;
        }

        return true;
    }

    public TimeZoneInfo GetTimeZoneOrDefault(ulong? guildId)
    {
        if (guildId is ulong gid && _timezones.TryGetValue(gid, out var tz))
            return tz;

        return null;
    }

    public async Task SetTimeZoneAsync(ulong guildId, TimeZoneInfo tz)
    {
        var tzId = tz?.Id;
        await using (var uow = _db.GetDbContext())
        {
            await uow.EnsureGuildConfigAsync(guildId);
            await uow.GetTable<GuildConfig>()
                .Where(x => x.GuildId == guildId)
                .Set(x => x.TimeZoneId, tzId)
                .UpdateAsync();
        }

        if (tz is null)
            _timezones.TryRemove(guildId, out _);
        else
            _timezones[guildId] = tz;
    }

    public TimeZoneInfo GetTimeZoneOrUtc(ulong? guildId)
        => GetTimeZoneOrDefault(guildId) ?? TimeZoneInfo.Utc;

    public async Task OnReadyAsync()
    {
        await using (var uow = _db.GetDbContext())
        {
            var rows = await uow.GetTable<GuildConfig>()
                .Where(Queries.GuildOnShard<GuildConfig>(x => x.GuildId, _shardData.TotalShards, _shardData.ShardId))
                .Where(x => x.TimeZoneId != null)
                .Select(x => new { x.GuildId, x.TimeZoneId })
                .ToListAsyncLinqToDB();

            foreach (var row in rows)
            {
                if (TryGetById(row.TimeZoneId) is { } tz)
                    _timezones[row.GuildId] = tz;
            }
        }

        await _repStore.Register("%server.time%",
            (IGuild g) =>
            {
                var to = TimeZoneInfo.Local;
                if (g is not null)
                {
                    to = GetTimeZoneOrDefault(g.Id) ?? TimeZoneInfo.Local;
                }

                return TimeZoneInfo.ConvertTime(DateTime.UtcNow, TimeZoneInfo.Utc, to).ToShortTimeString();
            });
    }
}