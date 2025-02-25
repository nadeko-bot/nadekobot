using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NadekoBot.Common.ModuleBehaviors;
using System.ComponentModel.DataAnnotations;

namespace NadekoBot.Modules.Xp;

public partial class Xp
{
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public class XpRateCommands : NadekoModule<GuildConfigXpService>
    {
        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task XpRate()
        {
            var rates = await _service.GetGuildXpRatesAsync(ctx.Guild.Id);
            if (rates.GuildConfig is null && !rates.ChannelRates.Any())
            {
                await Response().Pending(strs.xp_rate_none).SendAsync();
                return;
            }

            var eb = CreateEmbed()
                     .WithOkColor();
            if (rates.GuildConfig is not null)
            {
                eb.AddField(GetText(strs.xp_rate_server),
                    strs.xp_rate_amount_cooldown(
                        rates.GuildConfig.XpAmount,
                        rates.GuildConfig.Cooldown));
            }

            if (rates.ChannelRates.Any())
            {
                var channelRates = rates.ChannelRates
                                        .Select(c => $"<#{c.ChannelId}>: {GetRateString(c.XpAmount, c.Cooldown)}")
                                        .Join('\n');

                eb.AddField(GetText(strs.xp_rate_channels), channelRates);
            }

            await Response().Embed(eb).SendAsync();
        }

        private string GetRateString(int argXpAmount, float cd)
        {
            if (argXpAmount == 0 || cd == 0)
                return GetText(strs.xp_rate_no_gain);

            return GetText(strs.xp_rate_amount_cooldown(argXpAmount, Math.Round(cd, 1).ToString(Culture)));
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task XpRate(int amount, float minutes)
        {
            if (amount is < 0 or > 1000)
            {
                await Response().Error(strs.xp_rate_amount_invalid).SendAsync();
                return;
            }

            if (minutes is < 0 or > 1440)
            {
                await Response().Error(strs.xp_rate_cooldown_invalid).SendAsync();
                return;
            }

            await _service.SetGuildXpRateAsync(ctx.Guild.Id, amount, (int)Math.Ceiling(minutes));
            await Response().Confirm(strs.xp_rate_server_set(amount, minutes)).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task XpRate(IMessageChannel channel, int amount, float minutes)
        {
            if (amount is < 0 or > 1000)
            {
                await Response().Error(strs.xp_rate_amount_invalid).SendAsync();
                return;
            }

            if (minutes is < 0 or > 1440)
            {
                await Response().Error(strs.xp_rate_cooldown_invalid).SendAsync();
                return;
            }

            await _service.SetChannelXpRateAsync(ctx.Guild.Id, channel.Id, amount, (int)Math.Ceiling(minutes));
            await Response()
                  .Confirm(strs.xp_rate_channel_set(Format.Bold(channel.ToString()), amount, minutes))
                  .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task XpRateReset()
        {
            await _service.ResetGuildXpRateAsync(ctx.Guild.Id);
            await Response().Confirm(strs.xp_rate_server_reset).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task XpRateReset(IMessageChannel channel)
            => await XpRateReset(channel.Id);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task XpRateReset(ulong channelId)
        {
            await _service.ResetChannelXpRateAsync(ctx.Guild.Id, channelId);
            await Response().Confirm(strs.xp_rate_channel_reset($"<#{channelId}>")).SendAsync();
        }
    }
}

public class GuildConfigXpService : IReadyExecutor, INService
{
    private readonly DbService _db;

    public GuildConfigXpService(DbService db)
    {
        _db = db;
    }

    public async Task<(GuildXpConfig? GuildConfig, List<ChannelXpConfig> ChannelRates)> GetGuildXpRatesAsync(
        ulong guildId)
    {
        await using var uow = _db.GetDbContext();
        var guildConfig =
            await AsyncExtensions.FirstOrDefaultAsync(uow.GetTable<GuildXpConfig>(), x => x.GuildId == guildId);

        var channelRates = await AsyncExtensions.ToListAsync(uow.GetTable<ChannelXpConfig>()
                                                                .Where(x => x.GuildId == guildId));

        return (guildConfig, channelRates);
    }

    public async Task SetGuildXpRateAsync(ulong guildId, int amount, int cooldown)
    {
        await using var uow = _db.GetDbContext();
        await uow.GetTable<GuildXpConfig>()
                 .InsertOrUpdateAsync(() => new()
                     {
                         GuildId = guildId,
                         XpAmount = amount,
                         Cooldown = cooldown
                     },
                     (_) => new()
                     {
                         Cooldown = cooldown,
                         XpAmount = amount,
                         GuildId = guildId
                     },
                     () => new()
                     {
                         GuildId = guildId
                     });
    }

    public async Task SetChannelXpRateAsync(
        ulong guildId,
        ulong channelId,
        int amount,
        int cooldown)
    {
        await using var uow = _db.GetDbContext();
        await uow.GetTable<ChannelXpConfig>()
                 .InsertOrUpdateAsync(() => new()
                     {
                         GuildId = guildId,
                         ChannelId = channelId,
                         XpAmount = amount,
                         Cooldown = cooldown
                     },
                     (_) => new()
                     {
                         Cooldown = cooldown,
                         XpAmount = amount,
                         GuildId = guildId,
                         ChannelId = channelId
                     },
                     () => new()
                     {
                         GuildId = guildId,
                         ChannelId = channelId
                     });
    }

    public async Task<bool> ResetGuildXpRateAsync(ulong guildId)
    {
        await using var uow = _db.GetDbContext();
        var deleted = await uow.GetTable<GuildXpConfig>()
                               .Where(x => x.GuildId == guildId)
                               .DeleteAsync();
        return deleted > 0;
    }

    public async Task<bool> ResetChannelXpRateAsync(ulong guildId, ulong channelId)
    {
        await using var uow = _db.GetDbContext();
        var deleted = await uow.GetTable<ChannelXpConfig>()
                               .Where(x => x.GuildId == guildId && x.ChannelId == channelId)
                               .DeleteAsync();
        return deleted > 0;
    }

    public Task OnReadyAsync()
        => Task.CompletedTask;
}

public class GuildXpConfig
{
    [Key]
    public ulong GuildId { get; set; }

    public int XpAmount { get; set; }
    public int Cooldown { get; set; }
    public string? XpTemplateUrl { get; set; }
}

public sealed class GuildXpConfigEntity : IEntityTypeConfiguration<GuildXpConfig>
{
    public void Configure(EntityTypeBuilder<GuildXpConfig> builder)
    {
    }
}

public class ChannelXpConfig
{
    [Key]
    public int Id { get; set; }

    public ulong GuildId { get; set; }
    public ulong ChannelId { get; set; }
    public int XpAmount { get; set; }
    public float Cooldown { get; set; }
}

public sealed class ChannelXpConfigEntity : IEntityTypeConfiguration<ChannelXpConfig>
{
    public void Configure(EntityTypeBuilder<ChannelXpConfig> builder)
    {
        builder.HasAlternateKey(x => new
        {
            x.GuildId,
            x.ChannelId
        });
    }
}