using NadekoBot.Common.TypeReaders;
using NadekoBot.Modules.Gambling.Blackjack;
using NadekoBot.Modules.Gambling.Common;
using NadekoBot.Modules.Gambling.Services;

namespace NadekoBot.Modules.Gambling;

public partial class Gambling
{
    [Group]
    public sealed partial class BlackjackCommands(GamblingConfigService gamblingConfig, IBotCache cache)
        : GamblingModule<BlackjackService>(gamblingConfig)
    {
        // a repeat attempt during the cooldown gets no reply, so a member who spams errors sees one error per 2 seconds
        private static readonly TimeSpan _errorCooldown = TimeSpan.FromSeconds(2);

        [Cmd]
        [SpendsCurrency]
        [RequireContext(ContextType.Guild)]
        [BotPerm(ChannelPerm.SendMessages | ChannelPerm.EmbedLinks)]
        public async Task Blackjack([OverrideTypeReader(typeof(BalanceTypeReader))] long amount)
        {
            var errorKey = new TypedKey<bool>($"bj:err:{ctx.User.Id}");
            if ((await cache.GetAsync(errorKey)).IsT0)
                return;

            if (!await CheckBetMandatory(amount))
            {
                // a bet below 1 gets no reply, so the corrected bet must not be dropped
                if (amount >= 1)
                    await cache.AddAsync(errorKey, true, _errorCooldown);

                return;
            }

            var result = await _service.JoinAsync(ctx.Guild.Id, ctx.Channel.Id, ctx.User.Id, amount, prefix);
            if (result is BjJoinResult.Opened or BjJoinResult.Joined)
                return;

            var error = result switch
            {
                BjJoinResult.AlreadySeated => strs.bj_already_seated,
                BjJoinResult.NotEnough => strs.not_enough(CurrencySign),
                BjJoinResult.Full => strs.bj_table_full,
                BjJoinResult.Started => strs.bj_round_running,
                BjJoinResult.TooManyTables => strs.bj_too_many_tables(BlackjackService.MAX_TABLES_PER_SERVER),
                BjJoinResult.Closed => strs.bj_table_closed(Format.Code(prefix + BlackjackRenderer.COMMAND_SYNTAX)),
                _ => strs.bj_busy
            };

            // Busy and Closed tell the member to run the command again, so a cooldown would drop that retry
            if (result is not (BjJoinResult.Busy or BjJoinResult.Closed))
                await cache.AddAsync(errorKey, true, _errorCooldown);

            await Response().Error(error).SendAsync();
        }
    }
}
