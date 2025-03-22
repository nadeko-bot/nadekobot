namespace NadekoBot.Modules.Xp;

public partial class Xp
{
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public class XpRateCommands : NadekoModule<XpRateService>
    {
        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task XpRate()
        {
            var rates = await _service.GetGuildXpRatesAsync(ctx.Guild.Id);
            if (!rates.GuildRates.Any() && !rates.ChannelRates.Any())
            {
                await Response().Pending(strs.xp_rate_none).SendAsync();
                return;
            }

            await Response()
                .Paginated()
                .Items(rates.ChannelRates.GroupBy(x => x.ChannelId).ToList())
                .PageSize(5)
                .Page((items, _) =>
                {
                    var eb = CreateEmbed()
                        .WithOkColor();

                    if (rates.GuildRates is not { Count: <= 0 })
                    {
                        eb.AddField(GetText(strs.xp_rate_server),
                            rates.GuildRates
                                .Select(x => GetText(strs.xp_rate_str(x.RateType, x.XpAmount, x.Cooldown)))
                                .Join('\n'));
                    }

                    if (items.Any())
                    {
                        var channelRates = items
                            .Select(x => $"""
                                          <#{x.Key}>
                                          {x.Select(c => $"- {GetText(strs.xp_rate_str(c.RateType, c.XpAmount, c.Cooldown))}").Join('\n')}
                                          """)
                            .Join('\n');

                        eb.AddField(GetText(strs.xp_rate_channels), channelRates);
                    }

                    return eb;
                })
                .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task XpRate(XpRateType type, int amount, float minutes)
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

            await _service.SetGuildXpRateAsync(ctx.Guild.Id, type, amount, (int)Math.Ceiling(minutes));
            await Response().Confirm(strs.xp_rate_server_set(amount, minutes)).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task XpRate(IMessageChannel channel, XpRateType type, int amount, float minutes)
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

            await _service.SetChannelXpRateAsync(ctx.Guild.Id, type, channel.Id, amount, (int)Math.Ceiling(minutes));
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