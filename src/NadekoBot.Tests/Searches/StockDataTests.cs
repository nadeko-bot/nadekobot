#nullable enable
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Nadeko.Common;
using NadekoBot.Modules.Searches;
using NadekoBot.Modules.Searches.Charts;
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

    private sealed class FakeNasdaqHandler : HttpMessageHandler
    {
        public int Calls;
        public bool Fail;
        public readonly ConcurrentQueue<string> Requests = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            Requests.Enqueue(request.RequestUri!.PathAndQuery);
            if (Fail)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

            if (request.RequestUri.AbsolutePath == "/api/v3/coins/markets")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(COINGECKO_COINS) });

            var file = request.RequestUri!.AbsolutePath switch
            {
                "/api/quote/NVDA/historical" => "nvda-historical.json",
                "/api/quote/NVDA/chart" => "nvda-chart.json",
                "/api/quote/NVDA/summary" => "nvda-summary.json",
                "/api/quote/BRK.B/historical" => "brkb-historical.json",
                "/api/quote/BRK.B/chart" => "brkb-chart.json",
                "/api/quote/BRK.B/summary" => "brkb-summary.json",
                "/api/quote/BRK.B/info" => "brkb-info.json",
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

        calls = handler.Calls;
        Assert.That((await svc.GetQuoteAsync("ZZZZQQ")).AsT1, Is.EqualTo(StockError.NotFound));
        Assert.That(handler.Requests, Has.Some.EqualTo("/api/quote/ZZZZQQ/chart?assetclass=stocks")
            .And.Some.EqualTo("/api/quote/ZZZZQQ/chart?assetclass=etf"));
        calls = handler.Calls;
        Assert.That((await svc.GetQuoteAsync("zzzzqq")).AsT1, Is.EqualTo(StockError.NotFound));
        Assert.That(handler.Calls, Is.EqualTo(calls));
        Assert.That((await svc.GetQuoteAsync("<script>")).AsT1, Is.EqualTo(StockError.NotFound));
        Assert.That(handler.Calls, Is.EqualTo(calls), "text which is not a symbol sends no request");

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
}
