#nullable disable
using NadekoBot.Common.TypeReaders;
using NadekoBot.Modules.Gambling.Common;
using NadekoBot.Modules.Gambling.Common.Blackjack;
using NadekoBot.Modules.Gambling.Services;

namespace NadekoBot.Modules.Gambling;

public partial class Gambling
{
    public partial class BlackJackCommands : GamblingModule<BlackJackService>
    {
        public enum BjAction
        {
            Hit = int.MinValue,
            Stand,
            Double
        }

        private readonly ICurrencyService _cs;
        private readonly DbService _db;
        private IUserMessage msg;

        public BlackJackCommands(ICurrencyService cs, DbService db, GamblingConfigService gamblingConf)
            : base(gamblingConf)
        {
            _cs = cs;
            _db = db;
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task BlackJack([OverrideTypeReader(typeof(BalanceTypeReader))] long amount)
        {
            if (!await CheckBetMandatory(amount))
                return;

            var newBj = new Blackjack(_cs);
            Blackjack bj;
            if (newBj == (bj = _service.Games.GetOrAdd(ctx.Channel.Id, newBj)))
            {
                if (!await bj.Join(ctx.User, amount))
                {
                    _service.Games.TryRemove(ctx.Channel.Id, out _);
                    await Response().Error(strs.not_enough(CurrencySign)).SendAsync();
                    return;
                }

                bj.StateUpdated += Bj_StateUpdated;
                bj.GameEnded += Bj_GameEnded;
                bj.Start();

                await Response().NoReply().Confirm(strs.bj_created(ctx.User.ToString())).SendAsync();
            }
            else
            {
                if (await bj.Join(ctx.User, amount))
                    await Response().NoReply().Confirm(strs.bj_joined(ctx.User.ToString())).SendAsync();
                else
                {
                    Log.Information("{User} can't join a blackjack game as it's in {BlackjackState} state already",
                        ctx.User,
                        bj.State);
                }
            }

            await ctx.Message.DeleteAsync();
        }

        private Task Bj_GameEnded(Blackjack arg)
        {
            _service.Games.TryRemove(ctx.Channel.Id, out _);
            return Task.CompletedTask;
        }

        private async Task Bj_StateUpdated(Blackjack bj)
        {
            try
            {
                if (msg is not null)
                    _ = msg.DeleteAsync();

                var c = bj.Dealer.Cards.Select(x => x.GetEmojiString())
                          .ToList();
                var dealerIcon = "❔ ";
                if (bj.State == Blackjack.GameState.Ended)
                {
                    if (bj.Dealer.GetHandValue() == 21)
                        dealerIcon = "💰 ";
                    else if (bj.Dealer.GetHandValue() > 21)
                        dealerIcon = "💥 ";
                    else
                        dealerIcon = "🏁 ";
                }

                var cStr = string.Concat(c.Select(x => x[..^1] + " "));
                cStr += "\n" + string.Concat(c.Select(x => x.Last() + " "));
                var embed = CreateEmbed()
                               .WithOkColor()
                               .WithTitle("BlackJack")
                               .AddField($"{dealerIcon} Dealer's Hand | Value: {bj.Dealer.GetHandValue()}", cStr);

                if (bj.CurrentUser is not null)
                    embed.WithFooter($"Player to make a choice: {bj.CurrentUser.DiscordUser}");

                foreach (var p in bj.Players)
                {
                    c = p.Cards.Select(x => x.GetEmojiString()).ToList();
                    cStr = "-\t" + string.Concat(c.Select(x => x[..^1] + " "));
                    cStr += "\n-\t" + string.Concat(c.Select(x => x.Last() + " "));
                    var full = $"{p.DiscordUser.ToString().TrimTo(20)} | Bet: {N(p.Bet)} | Value: {p.GetHandValue()}";
                    if (bj.State == Blackjack.GameState.Ended)
                    {
                        if (p.State == User.UserState.Lost)
                            full = "❌ " + full;
                        else
                            full = "✅ " + full;
                    }
                    else if (p == bj.CurrentUser)
                        full = "▶ " + full;
                    else if (p.State == User.UserState.Stand)
                        full = "⏹ " + full;
                    else if (p.State == User.UserState.Bust)
                        full = "💥 " + full;
                    else if (p.State == User.UserState.Blackjack)
                        full = "💰 " + full;

                    embed.AddField(full, cStr);
                }

                msg = await Response().Embed(embed).SendAsync();
            }
            catch
            {
            }
        }

        private string UserToString(User x)
        {
            var playerName = x.State == User.UserState.Bust
                ? Format.Strikethrough(x.DiscordUser.ToString().TrimTo(30))
                : x.DiscordUser.ToString();

            // var hand = $"{string.Concat(x.Cards.Select(y => "〖" + y.GetEmojiString() + "〗"))}";


            return $"{playerName} | Bet: {x.Bet}\n";
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public Task Hit()
            => InternalBlackJack(BjAction.Hit);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public Task Stand()
            => InternalBlackJack(BjAction.Stand);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public Task Double()
            => InternalBlackJack(BjAction.Double);

        private async Task InternalBlackJack(BjAction a)
        {
            if (!_service.Games.TryGetValue(ctx.Channel.Id, out var bj))
                return;

            if (a == BjAction.Hit)
                await bj.Hit(ctx.User);
            else if (a == BjAction.Stand)
                await bj.Stand(ctx.User);
            else if (a == BjAction.Double)
            {
                if (!await bj.Double(ctx.User))
                    await Response().Error(strs.not_enough(CurrencySign)).SendAsync();
            }

            await ctx.Message.DeleteAsync();
        }
    }
}