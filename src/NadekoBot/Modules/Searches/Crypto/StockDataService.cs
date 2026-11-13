using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using NadekoBot.Modules.Searches.Charts;
using OneOf;

namespace NadekoBot.Modules.Searches;

public enum StockError
{
    NotFound,
    Unavailable
}

public sealed record StockQuote(
    string Symbol,
    string Name,
    string? Exchange,
    double Price,
    double Change,
    double ChangePercent,
    double? Volume,
    double? MarketCap,
    PriceRange? DayRange,
    PriceRange? YearRange,
    DateTimeOffset? Time,
    Candle[] Candles);

public sealed record StockMatch(string Symbol, string Name);

// Stock and ETF quotes and daily candles from the Nasdaq site.
public sealed class StockDataService(
    IHttpClientFactory httpFactory,
    IBotCache cache,
    CandleChart charts,
    TimeProvider? timeProvider = null) : INService
{
    public const int MAX_SYMBOL_LENGTH = 10;
    public const int MAX_QUERY_LENGTH = 50;
    private const int CHART_MONTHS = 3;
    private const int MAX_LOOKUPS = 20;
    private const int MAX_DAILY_ROWS = 100;
    private const int CHART_CACHE_SIZE = 32;
    private const int NOT_FOUND = 400;
    private const long MS_PER_MINUTE = 60_000;
    private const long MS_PER_DAY = 86_400_000;
    private const int SECONDS_PER_DAY = 86_400;
    private const int MINUTES_PER_DAY = 24 * 60;
    private const int SESSION_OPEN = (9 * 60) + 30;
    private const int SESSION_CLOSE = 16 * 60;
    private const string API = "https://api.nasdaq.com/api/";
    private const string STOCKS = "stocks";
    private const string ETF = "etf";
    private const string SEARCH_KEY_PREFIX = "stock:search:";

    private static readonly int _unixEpochDay = DateOnly.FromDateTime(DateTime.UnixEpoch).DayNumber;
    private static readonly TimeSpan _quoteExpiry = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan _missingExpiry = TimeSpan.FromMinutes(10);

    // A quote without daily candles or without its summary, so the missing part comes back soon after a failure.
    private static readonly TimeSpan _partialExpiry = TimeSpan.FromMinutes(1);

    private static readonly string[] _nameSuffixes =
        [" Common Stock", " Common Shares", " Ordinary Shares", " American Depositary Shares", " Depositary Shares"];

    private static readonly TimeZoneInfo? _newYork =
        TimeZoneInfo.TryFindSystemTimeZoneById("America/New_York", out var zone) ? zone : null;

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly SingleFlight<string, OneOf<StockQuote, StockError>> _loads = new();
    private readonly SingleFlight<string, OneOf<StockMatch, StockError>> _searches = new();
    private readonly LookupGate _gate = new(MAX_LOOKUPS,
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        timeProvider ?? TimeProvider.System);

    private readonly ChartCache _charts = new(CHART_CACHE_SIZE, _quoteExpiry, timeProvider ?? TimeProvider.System);

    private enum FetchStatus
    {
        Ok,
        Missing,
        Failed
    }

    private readonly record struct Fetched<T>(FetchStatus Status, T? Data);

    public static bool IsSymbol(ReadOnlySpan<char> text)
    {
        if (text.Length is 0 or > MAX_SYMBOL_LENGTH || !char.IsAsciiLetterOrDigit(text[0]))
            return false;

        foreach (var c in text)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-'))
                return false;
        }

        return true;
    }

    public async Task<OneOf<StockQuote, StockError>> GetQuoteAsync(string text)
    {
        var trimmed = text.AsSpan().Trim();
        if (!IsSymbol(trimmed))
            return StockError.NotFound;

        var symbol = trimmed.ContainsAnyInRange('a', 'z')
            ? string.Create(trimmed.Length, text, static (dst, src) => src.AsSpan().Trim().ToUpperInvariant(dst))
            : trimmed.Length == text.Length
                ? text
                : trimmed.ToString();

        if ((await cache.GetAsync(QuoteKey(symbol))).TryPickT0(out var cached, out _))
            return cached;

        if ((await cache.GetAsync(MissingKey(symbol))).IsT0)
            return StockError.NotFound;

        return await _loads.RunAsync(symbol, this, static (self, s) => self.LoadQuoteInternalAsync(s));
    }

    public async Task<OneOf<StockMatch, StockError>> SearchAsync(string text)
    {
        var query = text.Trim();
        if (query.Length is 0 or > MAX_QUERY_LENGTH)
            return StockError.NotFound;

        var key = SearchKey(query);
        if ((await cache.GetAsync(key)).TryPickT0(out var cached, out _))
            return cached.Symbol.Length > 0 ? cached : StockError.NotFound;

        return await _searches.RunAsync(key.Key,
            (self: this, query, key),
            static (state, _) => state.self.SearchInternalAsync(state.query, state.key));
    }

    private async Task<OneOf<StockMatch, StockError>> SearchInternalAsync(string query, TypedKey<StockMatch> key)
    {
        try
        {
            if (!TryEnterInternal())
                return StockError.Unavailable;

            var found = await GetNasdaqInternalAsync($"{API}autocomplete/slookup/10?search={Uri.EscapeDataString(query)}",
                FinanceJsonContext.Default.NasdaqEnvelopeNasdaqSearchItemArray);

            if (found.Status == FetchStatus.Failed)
                return StockError.Unavailable;

            var match = new StockMatch("", "");
            foreach (var item in found.Data ?? [])
            {
                if (item.Symbol is not { } symbol
                    || !IsSymbol(symbol)
                    || !(string.Equals(item.Asset, "STOCKS", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(item.Asset, "ETF", StringComparison.OrdinalIgnoreCase)))
                    continue;

                match = new(symbol, CleanName(item.Name ?? symbol));
                break;
            }

            await cache.AddAsync(key, match, _missingExpiry);
            return match.Symbol.Length > 0 ? match : StockError.NotFound;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Unable to search the stocks for {Query}", query);
            return StockError.Unavailable;
        }
    }

    public byte[]? GetChart(StockQuote quote)
    {
        if (quote.Candles.Length == 0)
            return null;

        ref readonly var last = ref quote.Candles[^1];
        var image = _charts.Get(quote.Symbol, last);
        if (image is not null)
            return image;

        try
        {
            image = charts.Render(quote.Candles, new(quote.Symbol, quote.Name, quote.Exchange));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Unable to draw the chart of {Symbol}", quote.Symbol);
            return null;
        }

        _charts.Set(quote.Symbol, last, image);
        return image;
    }

    private async Task<OneOf<StockQuote, StockError>> LoadQuoteInternalAsync(string symbol)
    {
        try
        {
            if (!TryEnterInternal())
                return StockError.Unavailable;

            var assetClass = STOCKS;
            var set = await LoadSetInternalAsync(symbol, assetClass);
            if (!set.Found && set.Missing)
            {
                assetClass = ETF;
                set = await LoadSetInternalAsync(symbol, assetClass);
            }

            if (!set.Found)
            {
                if (!set.Missing)
                    return StockError.Unavailable;

                await cache.AddAsync(MissingKey(symbol), true, _missingExpiry);
                return StockError.NotFound;
            }

            // Nasdaq has no intraday chart for some class shares (BRK.B), but it has their info.
            var live = set.Intraday.Data is { } intraday ? LiveQuote.From(intraday) : null;
            live ??= LiveQuote.From((await GetNasdaqInternalAsync($"{API}quote/{symbol}/info?assetclass={assetClass}",
                FinanceJsonContext.Default.NasdaqEnvelopeNasdaqInfo)).Data);

            if (live is not { } now)
                return StockError.Unavailable;

            PriceRange? dayRange = null;
            PriceRange? yearRange = null;
            double? marketCap = null;
            var summary = set.Summary.Data?.SummaryData;
            if (summary is not null)
            {
                dayRange = summary.TodayHighLow.Value.OrNull();
                yearRange = summary.FiftTwoWeekHighLow.Value.OrNull() ?? summary.FiftyTwoWeekHighLow.Value.OrNull();
                if (summary.MarketCap.Value > 0)
                    marketCap = summary.MarketCap.Value;
            }

            var candles = set.Daily.Data?.TradesTable?.Rows is { Length: > 0 } rows
                ? BuildCandlesInternal(rows, now.Points, now.Price, now.Volume, dayRange)
                : [];

            var quote = new StockQuote(symbol,
                CleanName(now.Name ?? symbol),
                now.Exchange ?? summary?.Exchange.Value,
                now.Price,
                now.Change,
                now.Percent,
                now.Volume > 0 ? now.Volume : null,
                marketCap,
                dayRange,
                yearRange,
                ParseNewYorkTime(now.Time),
                candles);

            GetChart(quote);

            var complete = candles.Length > 0 && set.Summary.Status == FetchStatus.Ok;
            await cache.AddAsync(QuoteKey(symbol), quote, complete ? _quoteExpiry : _partialExpiry);

            return quote;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Unable to load the stock {Symbol}", symbol);
            return StockError.Unavailable;
        }
    }

    private readonly record struct QuoteSet(
        Fetched<NasdaqHistory> Daily,
        Fetched<NasdaqIntradayChart> Intraday,
        Fetched<NasdaqSummary> Summary)
    {
        public bool Found
            => Daily.Status == FetchStatus.Ok || Intraday.Status == FetchStatus.Ok;

        public bool Missing
            => Daily.Status == FetchStatus.Missing || Intraday.Status == FetchStatus.Missing;
    }

    private readonly record struct LiveQuote(
        string? Name,
        string? Exchange,
        double Price,
        double Change,
        double Percent,
        double Volume,
        string? Time,
        NasdaqIntradayPoint[]? Points)
    {
        public static LiveQuote? From(NasdaqIntradayChart chart)
        {
            var price = chart.LastSalePrice;
            if (!(price > 0))
                return null;

            var previous = chart.PreviousClose;
            var change = previous > 0 ? price - previous : 0;
            var percent = previous > 0 ? change / previous * 100 : Finite(chart.PercentageChange);
            return new(chart.Company, chart.Exchange, price, change, percent, Finite(chart.Volume), chart.TimeAsOf, chart.Chart);
        }

        public static LiveQuote? From(NasdaqInfo? info)
        {
            if (info?.PrimaryData is not { LastSalePrice: > 0 } data)
                return null;

            return new(info.CompanyName,
                info.Exchange,
                data.LastSalePrice,
                Finite(data.NetChange),
                Finite(data.PercentageChange),
                Finite(data.Volume),
                data.LastTradeTimestamp,
                null);
        }

        private static double Finite(double value)
            => double.IsFinite(value) ? value : 0;
    }

    // The three requests go out together, so a stock costs one round trip.
    private async Task<QuoteSet> LoadSetInternalAsync(string symbol, string assetClass)
    {
        var today = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);
        var daily = GetNasdaqInternalAsync(
            string.Create(CultureInfo.InvariantCulture,
                $"{API}quote/{symbol}/historical?assetclass={assetClass}&fromdate={today.AddMonths(-CHART_MONTHS):yyyy-MM-dd}&todate={today:yyyy-MM-dd}&limit={MAX_DAILY_ROWS}"),
            FinanceJsonContext.Default.NasdaqEnvelopeNasdaqHistory);
        var intraday = GetNasdaqInternalAsync($"{API}quote/{symbol}/chart?assetclass={assetClass}",
            FinanceJsonContext.Default.NasdaqEnvelopeNasdaqIntradayChart);
        var summary = GetNasdaqInternalAsync($"{API}quote/{symbol}/summary?assetclass={assetClass}",
            FinanceJsonContext.Default.NasdaqEnvelopeNasdaqSummary);

        return new(await daily, await intraday, await summary);
    }

    // The daily candles, then a candle for the session of today when the daily data does not have it yet.
    private static Candle[] BuildCandlesInternal(
        NasdaqHistoryRow[] rows,
        NasdaqIntradayPoint[]? intraday,
        double price,
        double volume,
        PriceRange? dayRange)
    {
        var candles = new Candle[rows.Length + 1];
        var count = 0;
        var lastDay = long.MinValue;
        var descending = rows.Length > 1 && rows[0].Date > rows[^1].Date;
        for (var n = 0; n < rows.Length; n++)
        {
            ref readonly var row = ref rows[descending ? rows.Length - 1 - n : n];
            long day = row.Date.DayNumber - _unixEpochDay;
            if (row.Date == default
                || !(row.Open > 0 && row.High > 0 && row.Low > 0 && row.Close > 0)
                || day <= lastDay)
                continue;

            candles[count++] = new(day * SECONDS_PER_DAY,
                row.Open,
                Math.Max(row.High, Math.Max(row.Open, row.Close)),
                Math.Min(row.Low, Math.Min(row.Open, row.Close)),
                row.Close,
                row.Volume > 0 ? row.Volume : 0);
            lastDay = day;
        }
        if (TryBuildSessionCandleInternal(intraday ?? [], price, volume, dayRange, out var session)
            && session.Time / SECONDS_PER_DAY > lastDay)
            candles[count++] = session;

        return count == candles.Length ? candles : candles[..count];
    }

    // Open is the first minute at or after 9:30, close the last minute up to 16:00 (or the last sale while the session runs).
    private static bool TryBuildSessionCandleInternal(
        ReadOnlySpan<NasdaqIntradayPoint> points,
        double price,
        double volume,
        PriceRange? dayRange,
        out Candle candle)
    {
        candle = default;
        double open = 0, close = 0, high = 0, low = double.MaxValue;
        var day = -1L;
        var lastMinute = -1;
        foreach (ref readonly var point in points)
        {
            if (!(point.Y > 0))
                continue;

            var minute = (int)(point.X / MS_PER_MINUTE % MINUTES_PER_DAY);
            lastMinute = minute;
            if (minute is < SESSION_OPEN or > SESSION_CLOSE)
                continue;

            if (day < 0)
            {
                day = point.X / MS_PER_DAY;
                open = point.Y;
            }

            close = point.Y;
            high = Math.Max(high, point.Y);
            low = Math.Min(low, point.Y);
        }

        if (day < 0)
            return false;

        if (lastMinute <= SESSION_CLOSE && price > 0)
            close = price;

        if (dayRange is { } range)
        {
            high = range.High;
            low = range.Low;
        }

        candle = new(day * SECONDS_PER_DAY,
            open,
            Math.Max(high, Math.Max(open, close)),
            Math.Min(low, Math.Min(open, close)),
            close,
            volume);
        return true;
    }

    private async Task<Fetched<T>> GetNasdaqInternalAsync<T>(string url, JsonTypeInfo<NasdaqEnvelope<T>> info)
        where T : class
    {
        try
        {
            var envelope = await FinanceHttp.GetJsonAsync(httpFactory, url, info);
            if (envelope?.Data is { } data)
                return new(FetchStatus.Ok, data);

            return new(envelope?.Status?.Code == NOT_FOUND ? FetchStatus.Missing : FetchStatus.Failed, null);
        }
        catch (JsonException ex)
        {
            SiteAnswer.LogUnreadable("Nasdaq", ex);
            _gate.Pause();
            return new(FetchStatus.Failed, null);
        }
        catch (Exception ex)
        {
            Log.Warning("Nasdaq request {Url} failed: {ErrorMessage}", url, ex.Message);
            _gate.Pause();
            return new(FetchStatus.Failed, null);
        }
    }

    private bool TryEnterInternal()
    {
        if (_gate.TryEnter())
            return true;

        Log.Warning("Stock lookups are paused or over their limit");
        return false;
    }

    public static string CleanName(string name)
    {
        foreach (var suffix in _nameSuffixes)
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal))
                return name[..^suffix.Length].TrimEnd();
        }

        return name;
    }

    public static DateTimeOffset? ParseNewYorkTime(string? text)
    {
        if (_newYork is null || text is null)
            return null;

        var span = text.AsSpan().Trim();
        if (span.EndsWith(" ET", StringComparison.Ordinal))
            span = span[..^3];

        if (!DateTime.TryParseExact(span,
                "MMM d, yyyy h:mm tt",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var local))
            return null;

        return new DateTimeOffset(local, _newYork.GetUtcOffset(local));
    }

    private static TypedKey<StockQuote> QuoteKey(string symbol)
        => new($"stock:quote:{symbol}");

    private static TypedKey<bool> MissingKey(string symbol)
        => new($"stock:missing:{symbol}");

    private static TypedKey<StockMatch> SearchKey(string query)
        => new(string.Create(SEARCH_KEY_PREFIX.Length + query.Length,
            query,
            static (dst, src) =>
            {
                SEARCH_KEY_PREFIX.CopyTo(dst);
                src.AsSpan().ToLowerInvariant(dst[SEARCH_KEY_PREFIX.Length..]);
            }));
}
