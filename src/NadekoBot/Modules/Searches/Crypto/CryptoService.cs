#nullable enable
using System.Buffers;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using NadekoBot.Modules.Searches.Charts;
using NadekoBot.Modules.Searches.Common;

namespace NadekoBot.Modules.Searches.Services;

// The candles of a coin chart, which all shards share through the cache.
public sealed record CoinCandles(ChartFrame Frame, Candle[] Candles);

public class CryptoService(
    IBotCache cache,
    IHttpClientFactory httpFactory,
    IBotCreds creds,
    CandleChart charts,
    TimeProvider? timeProvider = null) : INService
{
    public const int MAX_COINS_PAGE = 24;
    private const int CHART_DAYS = 90;

    // A coin with fewer daily candles gets shorter candles, so that its chart has about 40 candles or more.
    private const int MIN_DAILY_CANDLES = 20;
    private const int MIN_FRAME_CANDLES = 40;
    private const int MAX_LOOKUPS = 20;
    private const int CHART_CACHE_SIZE = 32;
    private const int USD_ID = 2781;
    private const int SECONDS_PER_DAY = 86_400;
    private const string VENUE = "CoinMarketCap";
    private const string HISTORY_URL = "https://api.coinmarketcap.com/data-api/v3/cryptocurrency/historical";
    private const string TOP_COINS_URL =
        "https://api.coingecko.com/api/v3/coins/markets?vs_currency=usd&order=market_cap_desc&per_page=250";

    private static readonly TimeSpan _candleExpiry = TimeSpan.FromMinutes(15);

    // Daily candles of a young coin after its shorter candles failed, so that the shorter candles come back soon.
    private static readonly TimeSpan _partialCandleExpiry = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan _chartExpiry = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan _topCoinsExpiry = TimeSpan.FromHours(1);
    private static readonly TypedKey<IReadOnlyCollection<GeckoCoinsResult>> _topCoinsKey = new("crypto:top_coins");

    private static readonly TimeSpan _cryptoDataExpiry = TimeSpan.FromHours(2);
    private static readonly TimeSpan _retryDelay = TimeSpan.FromMinutes(5);
    private static readonly TypedKey<CryptoResponse> _cryptoDataKey = new("nadeko:crypto_data");

    private readonly SemaphoreSlim _getCryptoLock = new(1, 1);

    // Only read and written while the bot holds _getCryptoLock.
    private long _cryptoRetryAt;

    private long _coinsRetryAt;
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly SingleFlight<int, CoinCandles?> _candleLoads = new();
    private readonly LookupGate _gate = new(MAX_LOOKUPS,
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        timeProvider ?? TimeProvider.System);

    // The shorter candles of young coins have their own gate, so a failure there does not stop the daily charts.
    private readonly LookupGate _shorterGate = new(MAX_LOOKUPS,
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        timeProvider ?? TimeProvider.System);

    private static readonly ChartFrame[] _coinFrames = [ChartFrame.H4, ChartFrame.H1, ChartFrame.M15];

    private readonly ChartCache _charts = new(CHART_CACHE_SIZE, _chartExpiry, timeProvider ?? TimeProvider.System);

    public async Task<(CmcResponseData? Data, CmcResponseData? Nearest)> GetCryptoData(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return (null, null);

        charts.WarmUp();
        var cryptos = await GetCryptoDataInternal();

        if (cryptos is null or { Count: 0 })
            return (null, null);

        var crypto = cryptos.FirstOrDefault(x
            => string.Equals(x.Slug, name, StringComparison.InvariantCultureIgnoreCase)
               || string.Equals(x.Name, name, StringComparison.InvariantCultureIgnoreCase)
               || string.Equals(x.Symbol, name, StringComparison.InvariantCultureIgnoreCase));

        if (crypto is not null)
            return (crypto, null);

        CmcResponseData? nearest = null;
        var bestDistance = int.MaxValue;
        foreach (var elem in cryptos)
        {
            var d = elem.Name.LevenshteinDistance(name, ignoreCase: true);
            if (d < bestDistance)
            {
                bestDistance = d;
                nearest = elem;
                if (d == 0)
                    break;
            }
        }

        return (null, bestDistance <= 2 ? nearest : null);
    }

    // After a failure the bot waits 5 minutes, so a bot without a working key does not call the site for each command.
    public async Task<List<CmcResponseData>?> GetCryptoDataInternal()
    {
        try
        {
            if ((await cache.GetAsync(_cryptoDataKey)).TryPickT0(out var cached, out _))
                return cached.Data;

            await _getCryptoLock.WaitAsync();
            try
            {
                if ((await cache.GetAsync(_cryptoDataKey)).TryPickT0(out cached, out _))
                    return cached.Data;

                if (_time.GetUtcNow().UtcTicks < _cryptoRetryAt)
                    return null;

                var data = await LoadCryptoDataInternalAsync();
                if (data is null)
                {
                    _cryptoRetryAt = (_time.GetUtcNow() + _retryDelay).UtcTicks;
                    return null;
                }

                await cache.AddAsync(_cryptoDataKey, data, _cryptoDataExpiry);
                return data.Data;
            }
            finally
            {
                _getCryptoLock.Release();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving crypto data: {Message}", ex.Message);
            return null;
        }
    }

    private async Task<CryptoResponse?> LoadCryptoDataInternalAsync()
    {
        try
        {
            using var http = httpFactory.CreateClient();
            var data = await http.GetFromJsonAsync<CryptoResponse>(
                "https://pro-api.coinmarketcap.com/v1/cryptocurrency/listings/latest?"
                + $"CMC_PRO_API_KEY={creds.CoinmarketcapApiKey}"
                + "&start=1"
                + "&limit=5000"
                + "&convert=USD");

            if (data?.Data is null)
                return null;

            data.Data.RemoveAll(static x => x is null
                                            || string.IsNullOrWhiteSpace(x.Name)
                                            || string.IsNullOrWhiteSpace(x.Symbol)
                                            || string.IsNullOrWhiteSpace(x.Slug)
                                            || x.Quote is null
                                            || !x.Quote.TryGetValue("USD", out var usd)
                                            || usd is null);
            return data;
        }
        catch (JsonException ex)
        {
            SiteAnswer.LogUnreadable("CoinMarketCap", ex);
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting crypto data: {Message}", ex.Message);
            return null;
        }
    }

    // Only a daily chart takes the price of the coin list into its last candle, because the list can be 2 hours old.
    public async Task<byte[]?> GetChartAsync(CmcResponseData crypto, CmcQuote usd)
    {
        CoinCandles? history;
        try
        {
            history = await GetCandlesInternalAsync(crypto.Id);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Unable to load the candles of {Coin}", crypto.Slug);
            return null;
        }

        if (history is not { Candles.Length: > 0 })
            return null;

        var frame = history.Frame;
        var buffer = ArrayPool<Candle>.Shared.Rent(history.Candles.Length);
        try
        {
            var candles = buffer.AsSpan(0, history.Candles.Length);
            history.Candles.CopyTo(candles);

            ref var last = ref candles[^1];
            double? previousClose = null;
            if (usd.Price is { } price && price > 0)
            {
                if (frame == ChartFrame.Day)
                {
                    last = last with
                    {
                        High = Math.Max(last.High, price),
                        Low = Math.Min(last.Low, price),
                        Close = price,
                        Volume = usd.Volume24h is { } volume && volume > 0 ? volume : last.Volume
                    };
                }
                else if (usd.PercentChange24h is { } percent && percent > -100)
                {
                    // The title shows the 24h change percent of the embed.
                    previousClose = last.Close / (1 + (percent / 100));
                }
            }

            var key = crypto.Id.ToString(CultureInfo.InvariantCulture);
            var image = _charts.Get(key, frame, last);
            if (image is not null)
                return image;

            image = charts.Render(candles, new(crypto.Symbol, crypto.Name, VENUE, frame, previousClose));
            _charts.Set(key, frame, last, image);
            return image;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Unable to draw the chart of {Coin}", crypto.Slug);
            return null;
        }
        finally
        {
            ArrayPool<Candle>.Shared.Return(buffer);
        }
    }

    private async Task<CoinCandles?> GetCandlesInternalAsync(int id)
    {
        var key = new TypedKey<CoinCandles>($"crypto:chart:{id}");
        if ((await cache.GetAsync(key)).TryPickT0(out var cached, out _))
            return cached;

        return await _candleLoads.RunAsync(id,
            (self: this, key),
            static (state, coin) => state.self.LoadCandlesInternalAsync(coin, state.key));
    }

    // Daily candles of the last 90 days, and a candle for today from the hourly candles since 00:00 UTC.
    private async Task<CoinCandles?> LoadCandlesInternalAsync(int id, TypedKey<CoinCandles> key)
    {
        if (!_gate.TryEnter())
        {
            Log.Warning("Crypto chart lookups are paused or over their limit");
            return null;
        }

        try
        {
            var now = _time.GetUtcNow().ToUnixTimeSeconds();
            var today = now - (now % SECONDS_PER_DAY);
            var dailyTask = FinanceHttp.GetJsonAsync(httpFactory,
                HistoryUrl(id, today - (CHART_DAYS * SECONDS_PER_DAY), now, "1d"),
                FinanceJsonContext.Default.CmcHistoryEnvelope);
            var hourlyTask = FinanceHttp.GetJsonAsync(httpFactory,
                HistoryUrl(id, today, now, "1h"),
                FinanceJsonContext.Default.CmcHistoryEnvelope);

            await Task.WhenAll(dailyTask, hourlyTask);
            var daily = (await dailyTask)?.Data?.Quotes ?? [];
            var hourly = (await hourlyTask)?.Data?.Quotes ?? [];

            var candles = new Candle[daily.Length + 1];
            var count = 0;
            foreach (ref readonly var item in daily.AsSpan())
            {
                var time = item.TimeOpen.ToUnixTimeSeconds();
                var q = item.Quote;
                if (time >= today
                    || (count > 0 && time <= candles[count - 1].Time)
                    || !(q.Open > 0 && q.High > 0 && q.Low > 0 && q.Close > 0))
                    continue;

                candles[count++] = new(time,
                    q.Open,
                    Math.Max(q.High, Math.Max(q.Open, q.Close)),
                    Math.Min(q.Low, Math.Min(q.Open, q.Close)),
                    q.Close,
                    q.Volume);
            }

            double open = 0, high = 0, low = double.MaxValue, close = 0, volume = 0;
            foreach (ref readonly var item in hourly.AsSpan())
            {
                var q = item.Quote;
                if (!(q.Open > 0 && q.High > 0 && q.Low > 0 && q.Close > 0))
                    continue;

                if (open == 0)
                    open = q.Open;

                high = Math.Max(high, q.High);
                low = Math.Min(low, q.Low);
                close = q.Close;
                volume = q.Volume;
            }

            if (open == 0 && count > 0)
            {
                open = close = high = low = candles[count - 1].Close;
                volume = candles[count - 1].Volume;
            }

            if (open > 0)
            {
                candles[count++] = new(today,
                    open,
                    Math.Max(high, Math.Max(open, close)),
                    Math.Min(low, Math.Min(open, close)),
                    close,
                    volume);
            }

            var chart = new CoinCandles(ChartFrame.Day, count == candles.Length ? candles : candles[..count]);
            var expiry = _candleExpiry;
            if (count is > 0 and < MIN_DAILY_CANDLES)
            {
                var (shorter, failed) = await LoadShorterCandlesInternalAsync(id, candles[0].Time, now, hourly);
                if (failed)
                    expiry = _partialCandleExpiry;

                if (shorter is not null && shorter.Candles.Length > count)
                    chart = shorter;
            }

            await cache.AddAsync(key, chart, expiry);
            return chart;
        }
        catch (JsonException ex)
        {
            SiteAnswer.LogUnreadable("CoinMarketCap", ex);
            _gate.Pause();
            return null;
        }
        catch (Exception ex)
        {
            Log.Warning("CoinMarketCap history of {Coin} failed: {ErrorMessage}", id, ex.Message);
            _gate.Pause();
            return null;
        }
    }

    // The first daily candle starts at 00:00 of the first day, so a coin which started later can need the next smaller frame.
    private async Task<(CoinCandles? Chart, bool Failed)> LoadShorterCandlesInternalAsync(
        int id,
        long start,
        long now,
        CmcHistoryItem[] hourly)
    {
        var age = now - start;
        var index = age >= MIN_FRAME_CANDLES * ChartFrame.H4.Seconds() ? 0
            : age >= MIN_FRAME_CANDLES * ChartFrame.H1.Seconds() ? 1
            : 2;

        CoinCandles? best = null;
        for (; index < _coinFrames.Length; index++)
        {
            var frame = _coinFrames[index];
            var candles = await LoadFrameCandlesInternalAsync(id, frame, start, now, hourly);
            if (candles is null)
                return (best, true);

            if (best is null || candles.Length > best.Candles.Length)
                best = new(frame, candles);

            if (candles.Length >= MIN_FRAME_CANDLES)
                break;
        }

        return (best, false);
    }

    // Null when the request fails.
    private async Task<Candle[]?> LoadFrameCandlesInternalAsync(
        int id,
        ChartFrame frame,
        long start,
        long now,
        CmcHistoryItem[] hourly)
    {
        if (!_shorterGate.TryEnter())
        {
            Log.Warning("Crypto chart lookups of shorter candles are paused or over their limit");
            return null;
        }

        var interval = frame switch
        {
            ChartFrame.H4 => "4h",
            ChartFrame.H1 => "1h",
            _ => "15m"
        };

        try
        {
            var answer = await FinanceHttp.GetJsonAsync(httpFactory,
                HistoryUrl(id, start, now, interval),
                FinanceJsonContext.Default.CmcHistoryEnvelope);

            var quotes = answer?.Data?.Quotes ?? [];
            var seconds = frame.Seconds();
            var candles = new Candle[quotes.Length + hourly.Length];
            var count = 0;
            foreach (ref readonly var item in quotes.AsSpan())
            {
                var time = item.TimeOpen.ToUnixTimeSeconds();
                var bucket = time - (time % seconds);
                if (!IsValid(item.Quote) || (count > 0 && bucket <= candles[count - 1].Time))
                    continue;

                candles[count++] = ShortCandleInternal(bucket, item.Quote);
            }

            // The site gives only full 4h candles, so the hours after the last one come from the hourly candles of today.
            if (frame == ChartFrame.H4 && count > 0)
            {
                var end = candles[count - 1].Time + seconds;
                foreach (ref readonly var item in hourly.AsSpan())
                {
                    var time = item.TimeOpen.ToUnixTimeSeconds();
                    var q = item.Quote;
                    if (time < end || !IsValid(q))
                        continue;

                    var bucket = time - (time % seconds);
                    ref var last = ref candles[count - 1];
                    if (bucket > last.Time)
                        candles[count++] = ShortCandleInternal(bucket, q);
                    else if (bucket == last.Time)
                        last = last with { High = Math.Max(last.High, q.High), Low = Math.Min(last.Low, q.Low), Close = q.Close };
                }
            }

            return count == candles.Length ? candles : candles[..count];
        }
        catch (JsonException ex)
        {
            SiteAnswer.LogUnreadable("CoinMarketCap", ex);
            _shorterGate.Pause();
            return null;
        }
        catch (Exception ex)
        {
            Log.Warning("CoinMarketCap history of {Coin} failed: {ErrorMessage}", id, ex.Message);
            _shorterGate.Pause();
            return null;
        }
    }

    private static bool IsValid(in CmcOhlcv q)
        => q.Open > 0 && q.High > 0 && q.Low > 0 && q.Close > 0;

    // Volume is 0, because the site gives each candle the volume of the 24 hours before its end.
    private static Candle ShortCandleInternal(long time, in CmcOhlcv q)
        => new(time,
            q.Open,
            Math.Max(q.High, Math.Max(q.Open, q.Close)),
            Math.Min(q.Low, Math.Min(q.Open, q.Close)),
            q.Close,
            0);

    private static string HistoryUrl(int id, long start, long end, string interval)
        => string.Create(CultureInfo.InvariantCulture,
            $"{HISTORY_URL}?id={id}&convertId={USD_ID}&timeStart={start}&timeEnd={end}&interval={interval}");

    public async Task<IReadOnlyCollection<GeckoCoinsResult>?> GetTopCoins(int page)
    {
        if (page > MAX_COINS_PAGE)
            page = MAX_COINS_PAGE;

        var coins = (await cache.GetAsync(_topCoinsKey)).TryPickT0(out var cached, out _)
            ? cached
            : await LoadTopCoinsInternalAsync();

        return coins?.Skip(page * 10).Take(10).ToList();
    }

    // Not through GetOrAddAsync, because the memory cache keeps a null result until it expires.
    private async Task<IReadOnlyCollection<GeckoCoinsResult>?> LoadTopCoinsInternalAsync()
    {
        // CoinGecko answers 429 to too many requests, so after a failure the bot waits before it asks again.
        if (_time.GetUtcNow().UtcTicks < Interlocked.Read(ref _coinsRetryAt))
            return null;

        List<GeckoCoinsResult>? coins;
        try
        {
            using var http = httpFactory.CreateClient();
            http.AddFakeHeaders();
            coins = await http.GetFromJsonAsync<List<GeckoCoinsResult>>(TOP_COINS_URL);
        }
        catch (JsonException ex)
        {
            SiteAnswer.LogUnreadable("CoinGecko", ex);
            coins = null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Log.Warning("CoinGecko did not send the coin list: {Message}", ex.Message);
            coins = null;
        }

        if (coins is null)
        {
            Interlocked.Exchange(ref _coinsRetryAt, (_time.GetUtcNow() + _retryDelay).UtcTicks);
            return null;
        }

        coins.RemoveAll(static x => x is null || string.IsNullOrWhiteSpace(x.Name) || string.IsNullOrWhiteSpace(x.Symbol));
        await cache.AddAsync(_topCoinsKey, coins, _topCoinsExpiry);
        return coins;
    }
}

public sealed class GeckoCoinsResult
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("symbol")]
    public string? Symbol { get; init; }

    [JsonPropertyName("current_price")]
    public decimal? CurrentPrice { get; init; }

    [JsonPropertyName("price_change_percentage_24h")]
    public decimal? PercentChange24h { get; init; }

    [JsonPropertyName("market_cap")]
    public decimal? MarketCap { get; init; }

    [JsonPropertyName("circulating_supply")]
    public decimal? CirculatingSupply { get; init; }

    [JsonPropertyName("total_supply")]
    public decimal? TotalSupply { get; init; }

    [JsonPropertyName("market_cap_rank")]
    public int? MarketCapRank { get; init; }
}
