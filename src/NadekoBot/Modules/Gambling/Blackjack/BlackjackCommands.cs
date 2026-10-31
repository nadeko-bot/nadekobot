using NadekoBot.Common.TypeReaders;
using NadekoBot.Modules.Gambling.Blackjack;
using NadekoBot.Modules.Gambling.Common;
using NadekoBot.Modules.Gambling.Services;

namespace NadekoBot.Modules.Gambling;

public partial class Gambling
{
    [Group]
    public sealed partial class BlackjackCommands(GamblingConfigService gamblingConfig)
        : GamblingModule<BlackjackService>(gamblingConfig)
    {
        [Cmd]
        [SpendsCurrency]
        [Ratelimit(5)]
        [RequireContext(ContextType.Guild)]
        [BotPerm(ChannelPerm.SendMessages | ChannelPerm.EmbedLinks)]
        public async Task Blackjack([OverrideTypeReader(typeof(BalanceTypeReader))] long amount)
        {
            if (!await CheckBetMandatory(amount))
                return;

            var result = await _service.JoinAsync(ctx.Guild.Id, ctx.Channel.Id, ctx.User.Id, amount, prefix);

            switch (result)
            {
                case BjJoinResult.Opened:
                case BjJoinResult.Joined:
                    return;
                case BjJoinResult.AlreadySeated:
                    await Response().Error(strs.bj_already_seated).SendAsync();
                    return;
                case BjJoinResult.NotEnough:
                    await Response().Error(strs.not_enough(CurrencySign)).SendAsync();
                    return;
                case BjJoinResult.Full:
                    await Response().Error(strs.bj_table_full).SendAsync();
                    return;
                case BjJoinResult.Started:
                    await Response().Error(strs.bj_round_running).SendAsync();
                    return;
                case BjJoinResult.TooManyTables:
                    await Response().Error(strs.bj_too_many_tables(BlackjackService.MAX_TABLES_PER_SERVER)).SendAsync();
                    return;
                case BjJoinResult.Closed:
                    await Response().Error(strs.bj_table_closed(Format.Code(prefix + BlackjackRenderer.COMMAND_SYNTAX))).SendAsync();
                    return;
                default:
                    await Response().Error(strs.bj_busy).SendAsync();
                    return;
            }
        }
    }
}
