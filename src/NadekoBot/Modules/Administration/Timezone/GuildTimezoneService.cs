#nullable disable
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Db.Models;
using NadekoBot.Common.ModuleBehaviors;

namespace NadekoBot.Modules.Administration.Services;

public sealed class GuildTimezoneService : ITimezoneService, IReadyExecutor, INService
{
    private ConcurrentDictionary<ulong, TimeZoneInfo> _timezones;
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

    private static (ulong GuildId, TimeZoneInfo Timezone) GetTimezoneTuple(GuildConfig x)
    {
        TimeZoneInfo tz;
        try
        {
            tz = x.TimeZoneId is null ? null : TimeZoneInfo.FindSystemTimeZoneById(x.TimeZoneId);
        }
        catch
        {
            tz = null;
        }

        return (x.GuildId, Timezone: tz);
    }

    public TimeZoneInfo GetTimeZoneOrDefault(ulong? guildId)
    {
        if (guildId is ulong gid && _timezones.TryGetValue(gid, out var tz))
            return tz;

        return null;
    }

    public void SetTimeZone(ulong guildId, TimeZoneInfo tz)
    {
        using var uow = _db.GetDbContext();
        var gc = uow.GuildConfigsForId(guildId, set => set);

        gc.TimeZoneId = tz?.Id;
        uow.SaveChanges();

        if (tz is null)
            _timezones.TryRemove(guildId, out tz);
        else
            _timezones.AddOrUpdate(guildId, tz, (_, _) => tz);
    }

    public TimeZoneInfo GetTimeZoneOrUtc(ulong? guildId)
        => GetTimeZoneOrDefault(guildId) ?? TimeZoneInfo.Utc;

    public async Task OnReadyAsync()
    {
        await using var uow = _db.GetDbContext();
        _timezones = await uow.GetTable<GuildConfig>()
                              .Where(x => Queries.GuildOnShard(x.GuildId, _shardData.TotalShards, _shardData.ShardId))
                              .ToListAsyncLinqToDB()
                              .Fmap(x => x
                                         .Select(GetTimezoneTuple)
                                         .ToDictionary(x => x.GuildId, x => x.Timezone)
                                         .ToConcurrent());

        await _repStore.Register("%server.time%",
            (IGuild g) =>
            {
                var to = TimeZoneInfo.Local;
                if (g is not null)
                {
                    to = GetTimeZoneOrDefault(g.Id) ?? TimeZoneInfo.Local;
                }

                return TimeZoneInfo.ConvertTime(DateTime.UtcNow, TimeZoneInfo.Utc, to).ToString("HH:mm ")
                       + to.StandardName.GetInitials();
            });
    }
}