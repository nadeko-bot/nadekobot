#nullable disable
using NadekoBot.Common.TypeReaders;
using NadekoBot.Modules.Gambling.Common;
using NadekoBot.Modules.Gambling.Services;

namespace NadekoBot.Modules.Gambling;

public partial class Gambling
{
    [Group]
    public partial class PlantPickCommands : GamblingModule<PlantPickService>
    {
        private readonly ILogCommandService _logService;

        public PlantPickCommands(ILogCommandService logService, GamblingConfigService gss)
            : base(gss)
            => _logService = logService;

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task Pick(string pass = null)
        {
            if (!string.IsNullOrWhiteSpace(pass) && !pass.IsAlphaNumeric())
                return;

            var picked = await _service.PickAsync(ctx.Guild.Id, (ITextChannel)ctx.Channel, ctx.User.Id, pass);

            if (picked > 0)
            {
                var msg = await Response().NoReply().Confirm(strs.picked(N(picked), ctx.User)).SendAsync();
                msg.DeleteAfter(10);
            }

            if (((SocketGuild)ctx.Guild).CurrentUser.GuildPermissions.ManageMessages)
            {
                try
                {
                    _logService.AddDeleteIgnore(ctx.Message.Id);
                    await ctx.Message.DeleteAsync();
                }
                catch { }
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task Plant([OverrideTypeReader(typeof(BalanceTypeReader))] long amount, string pass = null)
        {
            if (amount < 1)
                return;

            if (!string.IsNullOrWhiteSpace(pass) && !pass.IsAlphaNumeric())
                return;

            if (((SocketGuild)ctx.Guild).CurrentUser.GuildPermissions.ManageMessages)
            {
                _logService.AddDeleteIgnore(ctx.Message.Id);
                await ctx.Message.DeleteAsync();
            }

            var success = await _service.PlantAsync(ctx.Guild.Id,
                (ITextChannel)ctx.Channel,
                ctx.User.Id,
                ctx.User.ToString(),
                amount,
                pass);

            if (!success)
                await Response().Error(strs.not_enough(CurrencySign)).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
#if GLOBAL_NADEKO
            [OwnerOnly]
#endif
        public async Task GenCurrency()
        {
            var enabled = await _service.ToggleCurrencyGeneration(ctx.Guild.Id, ctx.Channel.Id);
            if (enabled)
                await Response().Confirm(strs.curgen_enabled).SendAsync();
            else
                await Response().Confirm(strs.curgen_disabled).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        [OwnerOnly]
        public async Task GenCurList(int page = 1)
        {
            if (--page < 0)
                return;

            var enabledIn = await _service.GetAllGeneratingChannels();

            await Response()
                   .Paginated()
                   .Items(enabledIn.ToList())
                   .PageSize(9)
                   .CurrentPage(page)
                   .Page((items, _) =>
                   {
                       if (!items.Any())
                           return CreateEmbed().WithErrorColor().WithDescription("-");

                       return items.Aggregate(CreateEmbed().WithOkColor(),
                           (eb, i) => eb.AddField(i.GuildId.ToString(), i.ChannelId));
                   })
                   .SendAsync();
        }
    }
}