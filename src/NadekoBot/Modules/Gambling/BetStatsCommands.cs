#nullable disable
using NadekoBot.Modules.Gambling.Common;
using NadekoBot.Modules.Gambling.Services;
using NadekoBot.Modules.Xp.Services;

namespace NadekoBot.Modules.Gambling;

public partial class Gambling
{
    [Group]
    public sealed class BetStatsCommands : GamblingModule<UserBetStatsService>
    {
        private readonly GamblingTxTracker _gamblingTxTracker;
        private readonly IBotCache _cache;
        private readonly IUserService _userService;

        public BetStatsCommands(
            GamblingTxTracker gamblingTxTracker,
            GamblingConfigService gcs,
            IBotCache cache,
            IUserService userService)
            : base(gcs)
        {
            _gamblingTxTracker = gamblingTxTracker;
            _cache = cache;
            _userService = userService;
        }

        [Cmd]
        public async Task BetStatsReset(GamblingGame? game = null)
        {
            var price = await _service.GetResetStatsPriceAsync(ctx.User.Id, game);

            var result = await PromptUserConfirmAsync(CreateEmbed()
                .WithDescription(
                    $"""
                     Are you sure you want to reset your bet stats for **{GetGameName(game)}**?

                     It will cost you {N(price)}
                     """));

            if (!result)
                return;

            var success = await _service.ResetStatsAsync(ctx.User.Id, game);

            if (success)
            {
                await ctx.OkAsync();
            }
            else
            {
                await Response()
                      .Error(strs.not_enough(CurrencySign))
                      .SendAsync();
            }
        }

        private string GetGameName(GamblingGame? game)
        {
            if (game is null)
                return "all games";

            return game.ToString();
        }

        [Cmd]
        [Priority(3)]
        public async Task BetStats()
            => await BetStats(ctx.User, null);

        [Cmd]
        [Priority(2)]
        public async Task BetStats(GamblingGame game)
            => await BetStats(ctx.User, game);

        [Cmd]
        [Priority(1)]
        public async Task BetStats([Leftover] IUser user)
            => await BetStats(user, null);

        [Cmd]
        [Priority(0)]
        public async Task BetStats(IUser user, GamblingGame? game)
        {
            var stats = await _gamblingTxTracker.GetUserStatsAsync(user.Id, game);

            if (stats.Count == 0)
                stats = new()
                {
                    new()
                    {
                        TotalBet = 1
                    }
                };

            var eb = CreateEmbed()
                     .WithOkColor()
                     .WithAuthor(user)
                     .AddField("Total Won", N(stats.Sum(x => x.PaidOut)), true)
                     .AddField("Biggest Win", N(stats.Max(x => x.MaxWin)), true)
                     .AddField("Biggest Bet", N(stats.Max(x => x.MaxBet)), true)
                     .AddField("# Bets", stats.Sum(x => x.WinCount + x.LoseCount), true)
                     .AddField("Payout",
                         (stats.Sum(x => x.PaidOut) / stats.Sum(x => x.TotalBet)).ToString("P2", Culture),
                         true);
            if (game == null)
            {
                var favGame = stats.MaxBy(x => x.WinCount + x.LoseCount);
                eb.AddField("Favorite Game",
                    favGame.Game + "\n" + Format.Italics((favGame.WinCount + favGame.LoseCount) + " plays"),
                    true);
            }
            else
            {
                eb.WithDescription(game.ToString())
                  .AddField("# Wins", stats.Sum(x => x.WinCount), true);
            }

            await Response()
                  .Embed(eb)
                  .SendAsync();
        }

        private readonly record struct WinLbStat(
            int Rank,
            string User,
            GamblingGame Game,
            long MaxWin);

        private TypedKey<List<WinLbStat>> GetWinLbKey(int page)
            => new($"winlb:{page}");

        private async Task<IReadOnlyCollection<WinLbStat>> GetCachedWinLbAsync(int page)
        {
            return await _cache.GetOrAddAsync(GetWinLbKey(page),
                async () =>
                {
                    var items = await _service.GetWinLbAsync(page);

                    if (items.Count == 0)
                        return [];

                    var outputItems = new List<WinLbStat>(items.Count);
                    for (var i = 0; i < items.Count; i++)
                    {
                        var x = items[i];
                        var user = (await ctx.Client.GetUserAsync(x.UserId, CacheMode.CacheOnly))?.ToString()
                                   ?? (await _userService.GetUserAsync(x.UserId))?.Username
                                   ?? x.UserId.ToString();

                        if (user.StartsWith("??"))
                            user = x.UserId.ToString();

                        outputItems.Add(new WinLbStat(i + 1 + (page * 9), user, x.Game, x.MaxWin));
                    }

                    return outputItems;
                },
                expiry: TimeSpan.FromMinutes(5));
        }

        [Cmd]
        public async Task WinLb(int page = 1)
        {
            if (--page < 0)
                return;

            await Response()
                  .Paginated()
                  .PageItems(p => GetCachedWinLbAsync(p))
                  .PageSize(9)
                  .Page((items, curPage) =>
                  {
                      var eb = CreateEmbed()
                               .WithTitle(GetText(strs.winlb))
                               .WithOkColor();

                      if (items.Count == 0)
                      {
                          eb.WithDescription(GetText(strs.empty_page));
                          return eb;
                      }

                      for (var i = 0; i < items.Count; i++)
                      {
                          var item = items[i];
                          eb.AddField($"#{item.Rank} {item.User}",
                              $"{N(item.MaxWin)}\n`{item.Game.ToString().ToLower()}`",
                              true);
                      }

                      return eb;
                  })
                  .SendAsync();
        }

        [Cmd]
        public async Task GambleStats()
        {
            var stats = await _gamblingTxTracker.GetAllAsync();

            var eb = CreateEmbed()
                .WithOkColor();

            var str = "` Feature `｜`   Bet  `｜`Paid Out`｜`  RoI  `\n";
            str += "――――――――――――――――――――\n";
            foreach (var stat in stats)
            {
                var perc = (stat.PaidOut / stat.Bet).ToString("P2", Culture);
                str += $"`{stat.Feature.PadBoth(9)}`"
                       + $"｜`{stat.Bet.ToString("N0").PadLeft(8, ' ')}`"
                       + $"｜`{stat.PaidOut.ToString("N0").PadLeft(8, ' ')}`"
                       + $"｜`{perc.PadLeft(6, ' ')}`\n";
            }

            var bet = stats.Sum(x => x.Bet);
            var paidOut = stats.Sum(x => x.PaidOut);

            if (bet == 0)
                bet = 1;

            var tPerc = (paidOut / bet).ToString("P2", Culture);
            str += "――――――――――――――――――――\n";
            str += $"` {("TOTAL").PadBoth(7)}` "
                   + $"｜**{N(bet).PadLeft(8, ' ')}**"
                   + $"｜**{N(paidOut).PadLeft(8, ' ')}**"
                   + $"｜`{tPerc.PadLeft(6, ' ')}`";

            eb.WithDescription(str);

            await Response().Embed(eb).SendAsync();
        }

        [Cmd]
        [OwnerOnly]
        public async Task GambleStatsReset()
        {
            if (!await PromptUserConfirmAsync(CreateEmbed()
                    .WithDescription(
                        """
                        Are you sure?
                        This will completely reset Gambling Stats.

                        This action is irreversible.
                        """)))
                return;

            await GambleStats();
            await _service.ResetGamblingStatsAsync();

            await ctx.OkAsync();
        }
    }
}