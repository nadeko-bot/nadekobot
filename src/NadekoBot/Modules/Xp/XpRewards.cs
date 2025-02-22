using NadekoBot.Modules.Xp.Services;

namespace NadekoBot.Modules.Xp;

public partial class Xp
{
    public partial class XpRewards : NadekoModule<XpService>
    {
        private readonly ICurrencyProvider _cp;

        public XpRewards(ICurrencyProvider cp)
            => _cp = cp;

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task XpRewsReset()
        {
            var promptEmbed = CreateEmbed()
                .WithPendingColor()
                .WithDescription(GetText(strs.xprewsreset_confirm));

            var reply = await PromptUserConfirmAsync(promptEmbed);

            if (!reply)
                return;

            await _service.ResetXpRewards(ctx.Guild.Id);
            await ctx.OkAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task XpLevelUpRewards(int page = 1)
        {
            page--;

            if (page is < 0 or > 100)
                return;

            var xpSettings = await _service.GetFullXpSettingsFor(ctx.Guild.Id);
            var rews = xpSettings.RoleRewards;

            var allRewards = rews.OrderBy(x => x.Level)
                .Select(x =>
                {
                    var sign = !x.Remove ? "✅ " : "❌ ";

                    var str = ctx.Guild.GetRole(x.RoleId)?.ToString();

                    if (str is null)
                    {
                        str = GetText(strs.role_not_found(Format.Code(x.RoleId.ToString())));
                    }
                    else
                    {
                        if (!x.Remove)
                            str = GetText(strs.xp_receive_role(Format.Bold(str)));
                        else
                            str = GetText(strs.xp_lose_role(Format.Bold(str)));
                    }

                    return (x.Level, Text: sign + str);
                })
                .Concat(xpSettings.CurrencyRewards
                    .OrderBy(x => x.Level)
                    .Select(x => (x.Level,
                        Format.Bold(x.Amount + _cp.GetCurrencySign()))))
                .GroupBy(x => x.Level)
                .OrderBy(x => x.Key)
                .ToList();

            await Response()
                .Paginated()
                .Items(allRewards)
                .PageSize(9)
                .CurrentPage(page)
                .Page((items, _) =>
                {
                    var embed = CreateEmbed().WithTitle(GetText(strs.level_up_rewards)).WithOkColor();

                    if (!items.Any())
                        return embed.WithDescription(GetText(strs.no_level_up_rewards));

                    foreach (var reward in items)
                    {
                        embed.AddField(GetText(strs.level_x(reward.Key)),
                            string.Join("\n", reward.Select(y => y.Item2)));
                    }

                    return embed;
                })
                .SendAsync();
        }

        [Cmd]
        [UserPerm(GuildPerm.Administrator)]
        [BotPerm(GuildPerm.ManageRoles)]
        [RequireContext(ContextType.Guild)]
        [Priority(2)]
        public async Task XpRoleReward(int level)
        {
            await _service.ResetRoleRewardAsync(ctx.Guild.Id, level);
            await Response().Confirm(strs.xp_role_reward_cleared(level)).SendAsync();
        }

        [Cmd]
        [UserPerm(GuildPerm.Administrator)]
        [BotPerm(GuildPerm.ManageRoles)]
        [RequireContext(ContextType.Guild)]
        [Priority(1)]
        public async Task XpRoleReward(int level, AddRemove action, [Leftover] IRole role)
        {
            if (level < 1)
                return;

            await _service.SetRoleRewardAsync(ctx.Guild.Id, level, role.Id, action == AddRemove.Remove);
            if (action == AddRemove.Add)
            {
                await Response().Confirm(strs.xp_role_reward_add_role(level, Format.Bold(role.ToString()))).SendAsync();
            }
            else
            {
                await Response()
                    .Confirm(strs.xp_role_reward_remove_role(Format.Bold(level.ToString()),
                        Format.Bold(role.ToString())))
                    .SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [OwnerOnly]
        public async Task XpCurrencyReward(int level, int amount = 0)
        {
            if (level < 1 || amount < 0)
                return;

            await _service.SetCurrencyReward(ctx.Guild.Id, level, amount);
            if (amount == 0)
            {
                await Response().Confirm(strs.cur_reward_cleared(level, _cp.GetCurrencySign())).SendAsync();
            }
            else
            {
                await Response()
                    .Confirm(strs.cur_reward_added(level,
                        Format.Bold(amount + _cp.GetCurrencySign())))
                    .SendAsync();
            }
        }
    }
}