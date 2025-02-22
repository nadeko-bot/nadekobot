#nullable disable
using NadekoBot.Modules.Searches.Services;
using System.Globalization;

namespace NadekoBot.Modules.Searches;

public partial class Searches
{
    public partial class FinanceCommands : NadekoModule<CryptoService>
    {
        private readonly IStockDataService _stocksService;
        private readonly IStockChartDrawingService _stockDrawingService;

        public FinanceCommands(IStockDataService stocksService, IStockChartDrawingService stockDrawingService)
        {
            _stocksService = stocksService;
            _stockDrawingService = stockDrawingService;
        }

        [Cmd]
        public async Task Stock([Leftover] string query)
        {
            using var typing = ctx.Channel.EnterTypingState();

            var stock = await _stocksService.GetStockDataAsync(query);

            if (stock is null)
            {
                var symbols = await _stocksService.SearchSymbolAsync(query);

                if (symbols.Count == 0)
                {
                    await Response().Error(strs.not_found).SendAsync();
                    return;
                }

                var symbol = symbols.First();
                var promptEmbed = CreateEmbed()
                                         .WithDescription(symbol.Description)
                                         .WithTitle(GetText(strs.did_you_mean(symbol.Symbol)));

                if (!await PromptUserConfirmAsync(promptEmbed))
                    return;

                query = symbol.Symbol;
                stock = await _stocksService.GetStockDataAsync(query);

                if (stock is null)
                {
                    await Response().Error(strs.not_found).SendAsync();
                    return;
                }
            }

            var candles = await _stocksService.GetCandleDataAsync(query);
            var stockImageTask = _stockDrawingService.GenerateCombinedChartAsync(candles);

            var localCulture = (CultureInfo)Culture.Clone();
            localCulture.NumberFormat.CurrencySymbol = "$";

            var sign = stock.Price >= stock.Close
                ? "\\🔼"
                : "\\🔻";

            var change = (stock.Price - stock.Close).ToString("N2", Culture);
            var changePercent = (1 - (stock.Close / stock.Price)).ToString("P1", Culture);

            var price = stock.Price.ToString("C2", localCulture);

            var eb = CreateEmbed()
                            .WithOkColor()
                            .WithAuthor(stock.Symbol)
                            .WithUrl($"https://www.tradingview.com/chart/?symbol={stock.Symbol}")
                            .WithTitle(stock.Name)
                            .AddField(GetText(strs.price), $"{sign} **{price}**", true)
                            .AddField(GetText(strs.market_cap), stock.MarketCap, true)
                            .AddField(GetText(strs.volume_24h), stock.DailyVolume.ToString("C0", localCulture), true)
                            .AddField("Change", $"{change} ({changePercent})", true)
                            // .AddField("Change 50d", $"{sign50}{change50}", true)
                            // .AddField("Change 200d", $"{sign200}{change200}", true)
                            .WithFooter(stock.Exchange);

            var message = await Response().Embed(eb).SendAsync();
            await using var imageData = await stockImageTask;
            if (imageData is null)
                return;

            var fileName = $"{query}-sparkline.{imageData.Extension}";
            using var attachment = new FileAttachment(
                imageData.FileData,
                fileName
            );
            await message.ModifyAsync(mp =>
            {
                mp.Attachments =
                    new(new[] { attachment });

                mp.Embed = eb.WithImageUrl($"attachment://{fileName}").Build();
            });
        }


        [Cmd]
        public async Task Crypto(string name)
        {
            name = name?.ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(name))
                return;

            var (crypto, nearest) = await _service.GetCryptoData(name);

            if (nearest is not null)
            {
                var embed = CreateEmbed()
                                   .WithTitle(GetText(strs.crypto_not_found))
                                   .WithDescription(
                                       GetText(strs.did_you_mean(Format.Bold($"{nearest.Name} ({nearest.Symbol})"))));

                if (await PromptUserConfirmAsync(embed))
                    crypto = nearest;
            }

            if (crypto is null)
            {
                await Response().Error(strs.crypto_not_found).SendAsync();
                return;
            }

            var usd = crypto.Quote["USD"];

            var localCulture = (CultureInfo)Culture.Clone();
            localCulture.NumberFormat.CurrencySymbol = "$";

            var sevenDay = (usd.PercentChange7d / 100).ToString("P2", localCulture);
            var lastDay = (usd.PercentChange24h / 100).ToString("P2", localCulture);
            var price = usd.Price < 0.01
                ? usd.Price.ToString(localCulture)
                : usd.Price.ToString("C2", localCulture);

            var volume = usd.Volume24h.ToString("C0", localCulture);
            var marketCap = usd.MarketCap.ToString("C0", localCulture);
            var dominance = (usd.MarketCapDominance / 100).ToString("P2", localCulture);

            await using var sparkline = await _service.GetSparklineAsync(crypto.Id, usd.PercentChange7d >= 0);
            var fileName = $"{crypto.Slug}_7d.png";

            var toSend = CreateEmbed()
                                .WithOkColor()
                                .WithAuthor($"#{crypto.CmcRank}")
                                .WithTitle($"{crypto.Name} ({crypto.Symbol})")
                                .WithUrl($"https://coinmarketcap.com/currencies/{crypto.Slug}/")
                                .WithThumbnailUrl(
                                    $"https://s3.coinmarketcap.com/static/img/coins/128x128/{crypto.Id}.png")
                                .AddField(GetText(strs.market_cap), marketCap, true)
                                .AddField(GetText(strs.price), price, true)
                                .AddField(GetText(strs.volume_24h), volume, true)
                                .AddField(GetText(strs.change_7d_24h), $"{sevenDay} / {lastDay}", true)
                                .AddField(GetText(strs.market_cap_dominance), dominance, true)
                                .WithImageUrl($"attachment://{fileName}");

            if (crypto.CirculatingSupply is double cs)
            {
                var csStr = cs.ToString("N0", localCulture);

                if (crypto.MaxSupply is double ms)
                {
                    var perc = (cs / ms).ToString("P1", localCulture);

                    toSend.AddField(GetText(strs.circulating_supply), $"{csStr} ({perc})", true);
                }
                else
                {
                    toSend.AddField(GetText(strs.circulating_supply), csStr, true);
                }
            }


            await ctx.Channel.SendFileAsync(sparkline, fileName, embed: toSend.Build());
        }

        [Cmd]
        public async Task Coins(int page = 1)
        {
            if (--page < 0)
                return;

            if (page > 25)
                page = 25;

            await Response()
                  .Paginated()
                  .PageItems(async (page) =>
                  {
                      var coins = await _service.GetTopCoins(page);
                      return coins;
                  })
                  .PageSize(10)
                  .Page((items, _) =>
                  {
                      var embed = CreateEmbed()
                                         .WithOkColor();

                      if (items.Count > 0)
                      {
                          foreach (var coin in items)
                          {
                              embed.AddField($"#{coin.MarketCapRank} {coin.Symbol} - {coin.Name}",
                                  $"""
                                   `Price:` {GetArrowEmoji(coin.PercentChange24h)} {coin.CurrentPrice.ToShortString()}$ ({GetSign(coin.PercentChange24h)}{Math.Round(coin.PercentChange24h, 2)}%)
                                   `MarketCap:` {coin.MarketCap.ToShortString()}$
                                   `Supply:` {(coin.CirculatingSupply?.ToShortString() ?? "?")} / {(coin.TotalSupply?.ToShortString() ?? "?")}
                                   """,
                                  inline: false);
                          }
                      }

                      return embed;
                  })
                  .CurrentPage(page)
                  .AddFooter(false)
                  .SendAsync();
        }
        
        private static string GetArrowEmoji(decimal value)
            => value > 0 ? "▲" : "▼";

        private static string GetSign(decimal value)
            => value >= 0 ? "+" : "";
    }
}