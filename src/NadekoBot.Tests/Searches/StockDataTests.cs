#nullable enable
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.Extensions.Time.Testing;
using Nadeko.Common;
using NadekoBot.Modules.Searches;
using NadekoBot.Modules.Searches.Charts;
using NadekoBot.Modules.Searches.Common;
using NadekoBot.Modules.Searches.Services;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Searches;

// The answers in testdata/nasdaq were recorded from api.nasdaq.com on 13.11.2026 at 18:10 UTC, while the market was open.
public class StockDataTests
{
    private static readonly string _dir = Path.Combine(TestContext.CurrentContext.TestDirectory, "testdata", "nasdaq");

    // CoinGecko leaves out fields and sends null for some coins.
    private const string COINGECKO_COINS = """
        [
          {"id":"bitcoin","name":"Bitcoin","symbol":"btc","current_price":91234.5,"price_change_percentage_24h":-1.25,
           "market_cap":1810000000000,"circulating_supply":19950000,"total_supply":21000000,"market_cap_rank":1},
          {"id":"new-coin","name":"New Coin","symbol":"new","current_price":null,"market_cap":null,"market_cap_rank":null},
          {"id":"no-name","symbol":"nn","current_price":1}
        ]
        """;

    private const string QQQ_SEARCH = """{"data":[{"symbol":"QQQ","name":"Invesco QQQ Trust, Series 1","asset":"ETF"}],"status":{"rCode":200}}""";

    private const string ANT_NO_MINUTES = """
        {"data":{"company":"Anthropic, PBC Class A Common Stock","lastSalePrice":"$88.91","previousClose":"$97.17","chart":[]},"status":{"rCode":200}}
        """;

    // CoinMarketCap lists the coins from 00:00 UTC on 18.11.2026, and LATE_COIN from 23:30 UTC on 19.11.2026.
    private const int LATE_COIN = 98;
    private static readonly long _coinListed = new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
    private static readonly long _lateCoinListed = new DateTimeOffset(2026, 11, 19, 23, 30, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    private sealed class FakeNasdaqHandler : HttpMessageHandler
    {
        public int Calls;
        public bool Fail;
        public bool FailSessions;
        public bool NoMinutes;
        public string? SessionsReferer;
        public readonly ConcurrentQueue<string> Requests = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            Requests.Enqueue(request.RequestUri!.PathAndQuery);
            if (Fail)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

            if (request.RequestUri.AbsolutePath == "/api/v3/coins/markets")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(COINGECKO_COINS) });

            if (request.RequestUri.AbsolutePath == "/data-api/v3/cryptocurrency/historical")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(CoinHistory(request.RequestUri.Query)) });

            if (request.RequestUri.Host == "charting.nasdaq.com")
            {
                SessionsReferer = request.Headers.Referrer?.ToString();
                if (FailSessions)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(request.RequestUri.Query.Contains("symbol=ANT&", StringComparison.Ordinal)
                        ? File.ReadAllText(Path.Combine(_dir, "ant-intraday.json"))
                        : "{}")
                });
            }

            if (NoMinutes && request.RequestUri.AbsolutePath == "/api/quote/ANT/chart")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ANT_NO_MINUTES) });

            if (request.RequestUri.PathAndQuery == "/api/autocomplete/slookup/10?search=QQQ")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(QQQ_SEARCH) });

            // QQQ is an ETF with the answers of NVDA.
            var etf = request.RequestUri.Query.Contains("assetclass=etf", StringComparison.Ordinal);
            var file = request.RequestUri.AbsolutePath switch
            {
                "/api/quote/QQQ/historical" when etf => "nvda-historical.json",
                "/api/quote/QQQ/chart" when etf => "nvda-chart.json",
                "/api/quote/QQQ/summary" when etf => "nvda-summary.json",
                "/api/quote/NVDA/historical" => "nvda-historical.json",
                "/api/quote/NVDA/chart" => "nvda-chart.json",
                "/api/quote/NVDA/summary" => "nvda-summary.json",
                "/api/quote/BRK.B/historical" => "brkb-historical.json",
                "/api/quote/BRK.B/chart" => "brkb-chart.json",
                "/api/quote/BRK.B/summary" => "brkb-summary.json",
                "/api/quote/BRK.B/info" => "brkb-info.json",
                "/api/quote/ANT/historical" => "ant-historical.json",
                "/api/quote/ANT/chart" => "ant-chart.json",
                "/api/autocomplete/slookup/10" => "search-berkshire.json",
                _ => "unknown-chart.json"
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(File.ReadAllText(Path.Combine(_dir, file)))
            });
        }
    }

    [Test]
    public async Task QuotesSearchAndCoins_ReadTheSiteAnswers_AddTodaysCandle_AndCacheTheResult()
    {
        using var handler = new FakeNasdaqHandler();
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, false));
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 13, 18, 10, 0, TimeSpan.Zero));
        var svc = new StockDataService(factory, new MemoryBotCache(), new CandleChart(), time);

        // The summary has no AverageVolume, the field which made .stock fail.
        Assert.That(File.ReadAllText(Path.Combine(_dir, "nvda-summary.json")), Does.Not.Contain("AverageVolume"));
        var nvda = (await svc.GetQuoteAsync(" nvda ")).AsT0;
        Assert.That(nvda.Symbol, Is.EqualTo("NVDA"));
        Assert.That(nvda.Name, Is.EqualTo("NVIDIA Corporation"));
        Assert.That(nvda.Exchange, Is.EqualTo("NASDAQ-GS"));
        Assert.That(nvda.Price, Is.EqualTo(160.995));
        Assert.That(nvda.Change, Is.EqualTo(160.995 - 166.19).Within(1e-9));
        Assert.That(nvda.ChangePercent, Is.EqualTo((160.995 - 166.19) / 166.19 * 100).Within(1e-9));
        Assert.That(nvda.Volume, Is.EqualTo(252_963_495));
        Assert.That(nvda.MarketCap, Is.EqualTo(3_877_328_500_000));
        Assert.That(nvda.DayRange, Is.EqualTo(new PriceRange(158.39, 170)));
        Assert.That(nvda.YearRange, Is.EqualTo(new PriceRange(148.8, 243.37)));
        Assert.That(nvda.Time, Is.EqualTo(new DateTimeOffset(2026, 11, 13, 18, 10, 0, TimeSpan.Zero)));

        // 65 trading days, oldest first, then today: open at 9:30 ET, high and low of the day range, close at the price.
        Assert.That(nvda.Candles, Has.Length.EqualTo(66));
        Assert.That(nvda.Candles[0].Time, Is.EqualTo(new DateTimeOffset(2026, 8, 13, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds()));
        Assert.That(nvda.Candles[^2], Is.EqualTo(new Candle(
            new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(), 151.005, 167.14, 148.80, 166.19, 747_707_600)));
        Assert.That(nvda.Candles[^1], Is.EqualTo(new Candle(
            new DateTimeOffset(2026, 11, 13, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(), 167.215, 170, 158.39, 160.995, 252_963_495)));
        Assert.That(svc.GetChart(nvda)?.AsSpan(1, 3).ToArray(), Is.EqualTo("PNG"u8.ToArray()));

        var calls = handler.Calls;
        Assert.That((await svc.GetQuoteAsync("NVDA")).AsT0, Is.SameAs(nvda));
        Assert.That(handler.Calls, Is.EqualTo(calls));

        // Nasdaq has no intraday chart for BRK.B, so the price comes from its info and the chart ends with the last full day.
        var brk = (await svc.GetQuoteAsync("brk.b")).AsT0;
        Assert.That(brk.Name, Is.EqualTo("Berkshire Hathaway Inc."));
        Assert.That(brk.Price, Is.EqualTo(593.095));
        Assert.That(brk.Change, Is.EqualTo(3.265));
        Assert.That(brk.ChangePercent, Is.EqualTo(0.55));
        Assert.That(brk.Time, Is.EqualTo(new DateTimeOffset(2026, 11, 13, 18, 11, 0, TimeSpan.Zero)));
        Assert.That(brk.Candles, Has.Length.EqualTo(65));
        Assert.That(brk.Candles[^1].Close, Is.EqualTo(589.83));

        // A new symbol: the search runs with the stock requests. It names QQQ as an ETF, so the bot then asks for the ETF data.
        calls = handler.Calls;
        Assert.That((await svc.GetQuoteAsync("QQQ")).AsT0.Price, Is.EqualTo(160.995));
        Assert.That(handler.Requests, Has.Some.EqualTo("/api/autocomplete/slookup/10?search=QQQ")
            .And.Some.EqualTo("/api/quote/QQQ/chart?assetclass=etf"));
        Assert.That(handler.Calls, Is.EqualTo(calls + 7));

        // A symbol of known class costs one round: here the search named QQQ as an ETF before the lookup.
        var known = new StockDataService(factory, new MemoryBotCache(), new CandleChart(), time);
        await known.SearchAsync("QQQ");
        calls = handler.Calls;
        Assert.That((await known.GetQuoteAsync("QQQ")).IsT0);
        Assert.That(handler.Calls, Is.EqualTo(calls + 3));

        // The search does not name ZZZQ, so the bot does not ask for ETF data.
        calls = handler.Calls;
        Assert.That((await svc.GetQuoteAsync("ZZZQ")).AsT1, Is.EqualTo(StockError.NotFound));
        Assert.That(handler.Requests, Has.Some.EqualTo("/api/autocomplete/slookup/10?search=ZZZQ")
            .And.None.EqualTo("/api/quote/ZZZQ/chart?assetclass=etf"));
        Assert.That(handler.Calls, Is.EqualTo(calls + 4));
        calls = handler.Calls;
        Assert.That((await svc.GetQuoteAsync("zzzq")).AsT1, Is.EqualTo(StockError.NotFound));
        Assert.That(handler.Calls, Is.EqualTo(calls));
        Assert.That((await svc.GetQuoteAsync("<script>")).AsT1, Is.EqualTo(StockError.NotFound));
        Assert.That(handler.Calls, Is.EqualTo(calls), "text which is not a symbol sends no request");
        Assert.That(handler.Requests, Has.None.Contains("/data/charting/"), "a stock with 20 or more daily candles keeps the daily chart");

        // The search skips the mutual fund which Nasdaq lists first.
        var match = (await svc.SearchAsync(" Berkshire ")).AsT0;
        Assert.That(match, Is.EqualTo(new StockMatch("BRK.A", "Berkshire Hathaway Inc.")));
        Assert.That(handler.Requests, Has.Some.EqualTo("/api/autocomplete/slookup/10?search=Berkshire"));
        calls = handler.Calls;
        Assert.That((await svc.SearchAsync("berkshire")).AsT0, Is.EqualTo(match));
        Assert.That(handler.Calls, Is.EqualTo(calls));

        // A failed request pauses the lookups for 30 s.
        handler.Fail = true;
        Assert.That((await svc.GetQuoteAsync("AMD")).AsT1, Is.EqualTo(StockError.Unavailable));
        handler.Fail = false;
        calls = handler.Calls;
        Assert.That((await svc.GetQuoteAsync("TSLA")).AsT1, Is.EqualTo(StockError.Unavailable));
        Assert.That(handler.Calls, Is.EqualTo(calls));
        time.Advance(TimeSpan.FromSeconds(31));
        Assert.That((await svc.GetQuoteAsync("TSLA")).AsT1, Is.EqualTo(StockError.NotFound));
        Assert.That(handler.Calls, Is.GreaterThan(calls));

        // .coins shows a coin with missing values, and skips a coin without a name.
        var crypto = new CryptoService(new MemoryBotCache(), factory, Substitute.For<IBotCreds>(), new CandleChart(), time);
        handler.Fail = true;
        Assert.That(await crypto.GetTopCoins(0), Is.Null);
        handler.Fail = false;
        calls = handler.Calls;
        Assert.That(await crypto.GetTopCoins(0), Is.Null, "after an error the bot waits 5 minutes");
        Assert.That(handler.Calls, Is.EqualTo(calls));
        time.Advance(TimeSpan.FromMinutes(5));
        var coins = (await crypto.GetTopCoins(0))!.ToArray();
        Assert.That(coins.Select(x => x.Name), Is.EqualTo(new[] { "Bitcoin", "New Coin" }));
        Assert.That(coins[0].CurrentPrice, Is.EqualTo(91234.5m));
        Assert.That(coins[0].MarketCapRank, Is.EqualTo(1));
        Assert.That(coins[1].CurrentPrice, Is.Null);
        Assert.That(coins[1].MarketCapRank, Is.Null);
        calls = handler.Calls;
        Assert.That(await crypto.GetTopCoins(0), Has.Count.EqualTo(2));
        Assert.That(handler.Calls, Is.EqualTo(calls));
    }

    // ANT started to trade at 11:48 on 19.11.2026. ant-historical.json and ant-intraday.json (charting.nasdaq.com) were
    // recorded on 21.11.2026. The plain chart answer failed for ANT that day, so ant-chart.json has the points of its
    // charttype=rs answer.
    [Test]
    public async Task YoungStocksAndCoins_GetTheLargestShorterFrameWith40Candles()
    {
        using var handler = new FakeNasdaqHandler();
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, false));
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 21, 15, 0, 0, TimeSpan.Zero));

        // 2 daily candles. The 644 minutes of both sessions give 22 candles of 30m and 43 of 15m.
        // The 15:45 candle has the 16:00 minute, and it closes at the close of the day (97.17 and 88.91).
        var svc = new StockDataService(factory, new MemoryBotCache(), new CandleChart(), time);
        var ant = (await svc.GetQuoteAsync("ANT")).AsT0;
        Assert.That(handler.SessionsReferer, Is.EqualTo("https://charting.nasdaq.com/dynamic/chart.html"));
        Assert.That(ant.Frame, Is.EqualTo(ChartFrame.M15));
        Assert.That(ant.Candles, Has.Length.EqualTo(43));
        Assert.That(ant.Candles[0], Is.EqualTo(new Candle(WallClock(2026, 11, 19, 11, 45), 110, 113.760002, 105.190002, 105.190002, 277_265_664)));
        Assert.That(ant.Candles[16], Is.EqualTo(new Candle(WallClock(2026, 11, 19, 15, 45), 95.695, 97.775002, 95.695, 97.17, 46_319_616)));
        Assert.That(ant.Candles[^1], Is.EqualTo(new Candle(WallClock(2026, 11, 20, 15, 45), 86.410004, 89.300003, 86.410004, 88.91, 22_701_440)));
        Assert.That(svc.GetChart(ant)?.AsSpan(1, 3).ToArray(), Is.EqualTo("PNG"u8.ToArray()));

        // Without the sessions, the 391 minutes of 20.11 give 13 candles of 30m, 26 of 15m and 78 of 5m, without volume.
        handler.FailSessions = true;
        svc = new StockDataService(factory, new MemoryBotCache(), new CandleChart(), time);
        ant = (await svc.GetQuoteAsync("ANT")).AsT0;
        Assert.That(ant.Frame, Is.EqualTo(ChartFrame.M5));
        Assert.That(ant.Candles, Has.Length.EqualTo(78));
        Assert.That(ant.Candles[0], Is.EqualTo(new Candle(WallClock(2026, 11, 20, 9, 30), 88.175, 89.03, 87.6228, 87.6228, 0)));
        Assert.That(ant.Candles[^1].Close, Is.EqualTo(88.91), "the 16:00 minute has 88.75, the close of the day is 88.91");

        // Without any minute prices, the chart stays daily.
        handler.NoMinutes = true;
        svc = new StockDataService(factory, new MemoryBotCache(), new CandleChart(), time);
        ant = (await svc.GetQuoteAsync("ANT")).AsT0;
        Assert.That(ant.Frame, Is.EqualTo(ChartFrame.Day));
        Assert.That(ant.Candles, Has.Length.EqualTo(2));

        // A coin of 87 hours has 4 daily candles, so the bot asks for 1h candles from its first day.
        var crypto = new CryptoService(new MemoryBotCache(), factory, Substitute.For<IBotCreds>(), new CandleChart(), time);
        var coin = new CmcResponseData { Id = 99, Name = "New Coin", Symbol = "NEW", Slug = "new-coin" };
        var chart = await crypto.GetChartAsync(coin, new CmcQuote { Price = 10.5, PercentChange24h = 5 });
        Assert.That(chart?.AsSpan(1, 3).ToArray(), Is.EqualTo("PNG"u8.ToArray()));
        Assert.That(handler.Requests,
            Has.Some.Contains($"timeStart={_coinListed}&timeEnd={time.GetUtcNow().ToUnixTimeSeconds()}&interval=1h"));

        // A coin which started at 23:30: 61 hours after 00:00 of its first day, 1h gives 38 candles, so the bot asks for 15m.
        var early = new FakeTimeProvider(new DateTimeOffset(2026, 11, 21, 13, 0, 0, TimeSpan.Zero));
        crypto = new CryptoService(new MemoryBotCache(), factory, Substitute.For<IBotCreds>(), new CandleChart(), early);
        coin = new CmcResponseData { Id = LATE_COIN, Name = "Late Coin", Symbol = "LATE", Slug = "late-coin" };
        Assert.That(await crypto.GetChartAsync(coin, new CmcQuote { Price = 10.5, PercentChange24h = 5 }), Is.Not.Null);
        var firstDay = new DateTimeOffset(2026, 11, 19, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var range = $"id={LATE_COIN}&convertId=2781&timeStart={firstDay}&timeEnd={early.GetUtcNow().ToUnixTimeSeconds()}";
        Assert.That(handler.Requests, Has.Some.Contains(range + "&interval=1h").And.Some.Contains(range + "&interval=15m"));
    }

    private static long WallClock(int year, int month, int day, int hour, int minute)
        => new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    // The full candles of the asked range, from the candle in which the coin was listed.
    private static string CoinHistory(string query)
    {
        var args = HttpUtility.ParseQueryString(query);
        var listed = args["id"] == LATE_COIN.ToString(CultureInfo.InvariantCulture) ? _lateCoinListed : _coinListed;
        var end = long.Parse(args["timeEnd"]!, CultureInfo.InvariantCulture);
        var step = args["interval"] switch
        {
            "1d" => 86_400,
            "4h" => 14_400,
            "1h" => 3_600,
            _ => 900
        };

        var start = Math.Max(long.Parse(args["timeStart"]!, CultureInfo.InvariantCulture), listed);
        var quotes = new StringBuilder();
        for (var t = start - (start % step); t + step <= end; t += step)
        {
            if (quotes.Length > 0)
                quotes.Append(',');

            quotes.Append(CultureInfo.InvariantCulture,
                $$$"""{"timeOpen":"{{{DateTimeOffset.FromUnixTimeSeconds(t):yyyy-MM-dd'T'HH:mm:ss'Z'}}}","quote":{"open":10,"high":11,"low":9,"close":10.5,"volume":1000}}""");
        }

        return $$$"""{"data":{"quotes":[{{{quotes}}}]}}""";
    }
}
