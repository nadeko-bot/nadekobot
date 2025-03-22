using System.Runtime.CompilerServices;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Modules.Xp.Services;

namespace NadekoBot.Modules.Xp;

using GuildXpRates = (IReadOnlyList<GuildXpConfig> GuildRates, IReadOnlyList<ChannelXpConfig> ChannelRates);

public class XpRateService(DbService db, ShardData shardData, XpConfigService xcs) : IReadyExecutor, INService
{
    private ConcurrentDictionary<(XpRateType RateType, ulong GuildId), XpRate> _guildRates = new();
    private ConcurrentDictionary<ulong, ConcurrentDictionary<(XpRateType, ulong), XpRate>> _channelRates = new();

    public async Task OnReadyAsync()
    {
        await using var uow = db.GetDbContext();
        _guildRates = await uow.GetTable<GuildXpConfig>()
            .AsNoTracking()
            .Where(x => Queries.GuildOnShard(x.GuildId, shardData.TotalShards, shardData.ShardId))
            .ToListAsyncLinqToDB()
            .Fmap(list =>
                list
                    .ToDictionary(
                        x => (x.RateType, x.GuildId),
                        x => new XpRate(x.RateType, x.XpAmount, x.Cooldown))
                    .ToConcurrent());

        _channelRates = await uow.GetTable<ChannelXpConfig>()
            .AsNoTracking()
            .Where(x => Queries.GuildOnShard(x.GuildId, shardData.TotalShards, shardData.ShardId))
            .ToListAsyncLinqToDB()
            .Fmap(x =>
                x.GroupBy(x => x.GuildId)
                    .ToDictionary(
                        x => x.Key,
                        x => x.ToDictionary(
                                y => (y.RateType, y.ChannelId),
                                y => new XpRate(y.RateType, y.XpAmount, y.Cooldown))
                            .ToConcurrent())
                    .ToConcurrent());
    }

    public async Task<GuildXpRates> GetGuildXpRatesAsync(ulong guildId)
    {
        await using var uow = db.GetDbContext();
        var guildConfig = await uow.GetTable<GuildXpConfig>()
            .AsNoTracking()
            .Where(x => x.GuildId == guildId)
            .ToListAsyncLinqToDB();

        var channelRates = await uow.GetTable<ChannelXpConfig>()
            .AsNoTracking()
            .Where(x => x.GuildId == guildId)
            .ToListAsyncLinqToDB();

        return (guildConfig, channelRates);
    }

    public async Task SetGuildXpRateAsync(ulong guildId, XpRateType type, long amount, float cooldown)
    {
        AmountAndCooldownChecks(amount, cooldown);

        if (type == XpRateType.Voice)
            cooldown = 1.0f;

        await using var uow = db.GetDbContext();
        await uow.GetTable<GuildXpConfig>()
            .InsertOrUpdateAsync(() => new()
                {
                    GuildId = guildId,
                    RateType = type,
                    XpAmount = amount,
                    Cooldown = cooldown,
                },
                (_) => new()
                {
                    Cooldown = cooldown,
                    XpAmount = amount,
                },
                () => new()
                {
                    GuildId = guildId,
                    RateType = type,
                });

        _guildRates[(type, guildId)] = new XpRate(type, amount, cooldown);
    }

    public async Task SetChannelXpRateAsync(ulong guildId,
        XpRateType type,
        ulong channelId,
        long amount,
        float cooldown)
    {
        AmountAndCooldownChecks(amount, cooldown);

        if (type == XpRateType.Voice)
            cooldown = 1.0f;

        await using var uow = db.GetDbContext();
        await uow.GetTable<ChannelXpConfig>()
            .InsertOrUpdateAsync(() => new()
                {
                    GuildId = guildId,
                    ChannelId = channelId,
                    XpAmount = amount,
                    Cooldown = cooldown,
                    RateType = type
                },
                (_) => new()
                {
                    Cooldown = cooldown,
                    XpAmount = amount,
                },
                () => new()
                {
                    GuildId = guildId,
                    ChannelId = channelId,
                    RateType = type,
                });

        _channelRates.GetOrAdd(guildId, _ => new())
            [(type, channelId)] = new XpRate(type, amount, cooldown);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AmountAndCooldownChecks(long amount, float cooldown)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount, nameof(amount));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(amount, 1000, nameof(amount));

        ArgumentOutOfRangeException.ThrowIfNegative(cooldown, nameof(cooldown));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(cooldown, 1440, nameof(cooldown));
    }

    public async Task<bool> ResetGuildXpRateAsync(ulong guildId)
    {
        await using var uow = db.GetDbContext();
        var deleted = await uow.GetTable<GuildXpConfig>()
            .Where(x => x.GuildId == guildId)
            .DeleteAsync();

        _guildRates.TryRemove((XpRateType.Text, guildId), out _);

        return deleted > 0;
    }

    public async Task<bool> ResetChannelXpRateAsync(ulong guildId, ulong channelId)
    {
        await using var uow = db.GetDbContext();
        var deleted = await uow.GetTable<ChannelXpConfig>()
            .Where(x => x.GuildId == guildId && x.ChannelId == channelId)
            .DeleteAsync();

        if (_channelRates.TryGetValue(guildId, out var channelRates))
            channelRates.TryRemove((XpRateType.Text, channelId), out _);

        return deleted > 0;
    }

    public (XpRate Rate, bool IsItemRate) GetXpRate(XpRateType type, ulong guildId, ulong channelId)
    {
        if (_channelRates.TryGetValue(guildId, out var guildChannelRates))
        {
            if (guildChannelRates.TryGetValue((type, channelId), out var rate))
                return (rate, true);
        }

        if (_guildRates.TryGetValue((type, guildId), out var guildRate))
            return (guildRate, false);

        var conf = xcs.Data;
        
        var toReturn = type switch
        {
            XpRateType.Image => new XpRate(XpRateType.Image, conf.TextXpFromImage, conf.TextXpCooldown / 60.0f),
            XpRateType.Voice => new XpRate(XpRateType.Voice, conf.VoiceXpPerMinute, 1.0f),
            _ => new XpRate(XpRateType.Text, conf.TextXpPerMessage, conf.TextXpCooldown / 60.0f),
        };

        return (toReturn, false);
    }
}