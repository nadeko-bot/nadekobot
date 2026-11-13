using System.Globalization;
using NadekoBot.Modules.Searches.Charts;
using NadekoBot.Modules.Searches.Services;
using OneOf;

namespace NadekoBot.Modules.Searches;

public partial class Searches
{
    public partial class FinanceCommands(StockDataService stocks) : NadekoModule<CryptoService>
    {
        private static readonly Color _upColor = new(CandleChart.UP);
        private static readonly Color _downColor = new(CandleChart.DOWN);

        [Cmd]
        [BotPerm(ChannelPerm.EmbedLinks | ChannelPerm.AttachFiles)]
        public async Task Stock([Leftover] string query)
        {
            query = query.Trim();
            if (query.Length == 0)
                return;

            OneOf<StockQuote, StockError> result;
            OneOf<StockMatch, StockError>? search = null;
            using (ctx.Channel.EnterTypingState())
            {
                result = StockDataService.IsSymbol(query)
                    ? await stocks.GetQuoteAsync(query)
                    : StockError.NotFound;

                if (result.TryPickT1(out var notFound, out _) && notFound == StockError.NotFound)
                    search = await stocks.SearchAsync(query);
            }

            StockError error;
            if (search is { } found)
            {
                if (found.TryPickT1(out error, out var match))
                {
                    await SendStockErrorInternalAsync(error);
                    return;
                }

                if (!string.Equals(match.Symbol, query, StringComparison.OrdinalIgnoreCase))
                {
                    var prompt = CreateEmbed()
                        .WithTitle(GetText(strs.did_you_mean(match.Symbol)))
                        .WithDescription(match.Name);

                    if (!await PromptUserConfirmAsync(prompt))
                        return;
                }

                using (ctx.Channel.EnterTypingState())
                    result = await stocks.GetQuoteAsync(match.Symbol);
            }

            if (result.TryPickT1(out error, out var quote))
            {
                await SendStockErrorInternalAsync(error);
                return;
            }

            var decimals = ChartFormat.PriceDecimals(quote.Price);
            var format = ChartFormat.NumberFormat(decimals);
            var up = quote.Change >= 0;
            var sign = up ? "+" : "-";

            var eb = CreateEmbed()
                .WithColor(up ? _upColor : _downColor)
                .WithAuthor(quote.Symbol)
                .WithTitle(quote.Name)
                .WithUrl($"https://www.tradingview.com/chart/?symbol={Uri.EscapeDataString(quote.Symbol)}")
                .AddField(GetText(strs.price),
                    $"{(up ? "\\🔼" : "\\🔻")} **${quote.Price.ToString(format, Culture)}**",
                    true)
                .AddField(GetText(strs.change),
                    $"{sign}{Math.Abs(quote.Change).ToString(format, Culture)} ({sign}{Math.Abs(quote.ChangePercent).ToString("N2", Culture)}%)",
                    true);

            if (quote.Volume is { } volume)
                eb.AddField(GetText(strs.volume), ChartFormat.ShortString(volume, Culture), true);

            if (quote.MarketCap is { } marketCap)
                eb.AddField(GetText(strs.market_cap), "$" + ChartFormat.ShortString(marketCap, Culture), true);

            if (quote.DayRange is { } day)
                eb.AddField(GetText(strs.day_range), FormatRange(day, format), true);

            if (quote.YearRange is { } year)
                eb.AddField(GetText(strs.year_range), FormatRange(year, format), true);

            if (!string.IsNullOrEmpty(quote.Exchange))
                eb.WithFooter(quote.Exchange);

            if (quote.Time is { } time)
                eb.WithTimestamp(time);

            var chart = stocks.GetChart(quote);
            if (chart is null)
            {
                await Response().Embed(eb).SendAsync();
                return;
            }

            var fileName = $"{quote.Symbol}-chart.png";
            eb.WithImageUrl($"attachment://{fileName}");
            await using var image = new MemoryStream(chart, false);
            await Response().Embed(eb).File(image, fileName).SendAsync();
        }

        private string FormatRange(PriceRange range, string format)
            => $"{range.Low.ToString(format, Culture)} - {range.High.ToString(format, Culture)}";

        private Task SendStockErrorInternalAsync(StockError error)
            => error == StockError.Unavailable
                ? Response().Error(strs.stock_unavailable).SendAsync()
                : Response().Error(strs.not_found).SendAsync();

        [Cmd]
        [BotPerm(ChannelPerm.EmbedLinks | ChannelPerm.AttachFiles)]
        public async Task Crypto([Leftover] string name)
        {
            name = name.Trim();
            if (name.Length == 0)
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

            var sevenDay = FormatOrUnknown(usd.PercentChange7d / 100, "P2", localCulture);
            var lastDay = FormatOrUnknown(usd.PercentChange24h / 100, "P2", localCulture);
            var price = usd.Price is not { } p ? "?"
                : p < 0.01 ? p.ToString(localCulture)
                : p.ToString("C2", localCulture);

            var volume = FormatOrUnknown(usd.Volume24h, "C0", localCulture);
            var marketCap = FormatOrUnknown(usd.MarketCap, "C0", localCulture);
            var dominance = FormatOrUnknown(usd.MarketCapDominance / 100, "P2", localCulture);

            var toSend = CreateEmbed()
                .WithOkColor()
                .WithAuthor($"#{crypto.CmcRank?.ToString() ?? "?"}")
                .WithTitle($"{crypto.Name} ({crypto.Symbol})")
                .WithUrl($"https://coinmarketcap.com/currencies/{crypto.Slug}/")
                .WithThumbnailUrl(
                    $"https://s3.coinmarketcap.com/static/img/coins/128x128/{crypto.Id}.png")
                .AddField(GetText(strs.market_cap), marketCap, true)
                .AddField(GetText(strs.price), price, true)
                .AddField(GetText(strs.volume_24h), volume, true)
                .AddField(GetText(strs.change_7d_24h), $"{sevenDay} / {lastDay}", true)
                .AddField(GetText(strs.market_cap_dominance), dominance, true);

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

            var chart = await _service.GetChartAsync(crypto, usd);
            if (chart is null)
            {
                await Response().Embed(toSend).SendAsync();
                return;
            }

            var fileName = $"{crypto.Slug}-chart.png";
            toSend.WithImageUrl($"attachment://{fileName}");
            await using var image = new MemoryStream(chart, false);
            await Response().Embed(toSend).File(image, fileName).SendAsync();
        }

        [Cmd]
        public async Task Coins(int page = 1)
        {
            if (--page < 0)
                return;

            if (page > CryptoService.MAX_COINS_PAGE)
                page = CryptoService.MAX_COINS_PAGE;

            await Response()
                  .Paginated()
                  .PageItems(async (page) =>
                  {
                      var coins = await _service.GetTopCoins(page);
                      return coins ?? [];
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
                              var arrow = coin.PercentChange24h is { } up ? GetArrowEmoji(up) : string.Empty;
                              var change = coin.PercentChange24h is { } c ? $"{GetSign(c)}{Math.Round(c, 2)}%" : "?";
                              embed.AddField($"#{coin.MarketCapRank?.ToString() ?? "?"} {coin.Symbol} - {coin.Name}",
                                  $"""
                                   `Price:` {arrow} {coin.CurrentPrice?.ToShortString() ?? "?"}$ ({change})
                                   `MarketCap:` {coin.MarketCap?.ToShortString() ?? "?"}$
                                   `Supply:` {(coin.CirculatingSupply?.ToShortString() ?? "?")} / {(coin.TotalSupply?.ToShortString() ?? "?")}
                                   """,
                                  inline: false);
                          }
                      }
                      else
                      {
                          embed.WithDescription(GetText(strs.not_found));
                      }

                      return embed;
                  })
                  .CurrentPage(page)
                  .AddFooter(false)
                  .SendAsync();
        }

        private static string FormatOrUnknown(double? value, string format, CultureInfo culture)
            => value?.ToString(format, culture) ?? "?";

        private static string GetArrowEmoji(decimal value)
            => value > 0 ? "▲" : "▼";

        private static string GetSign(decimal value)
            => value >= 0 ? "+" : "";
    }
}
