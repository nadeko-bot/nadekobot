using AngleSharp;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace NadekoBot.Modules.Searches;

public sealed class DefaultStockDataService : IStockDataService, INService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IBotCache _cache;

    public DefaultStockDataService(IHttpClientFactory httpClientFactory, IBotCache cache)
        => (_httpClientFactory, _cache) = (httpClientFactory, cache);

    private static TypedKey<StockData> GetStockDataKey(string query)
        => new($"stockdata:{query}");

    public async Task<StockData?> GetStockDataAsync(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        return await _cache.GetOrAddAsync(GetStockDataKey(query.Trim().ToLowerInvariant()),
            () => GetStockDataInternalAsync(query),
            expiry: TimeSpan.FromHours(1));
    }

    public async Task<StockData?> GetStockDataInternalAsync(string query)
    {
        try
        {
            if (!query.IsAlphaNumeric())
                return default;

            var sum = await GetNasdaqDataResponse<NasdaqSummaryResponse>(
                $"https://api.nasdaq.com/api/quote/{query}/summary?assetclass=stocks");

            if (sum?.Data is not { } d || d.SummaryData is not { } sd)
                return default;

            var closePrice = double.Parse(sd.PreviousClose.Value?.Substring(1) ?? "0",
                NumberStyles.Any,
                CultureInfo.InvariantCulture);

            var info = await GetNasdaqDataResponse<NasdaqInfoResponse>(
                $"https://api.nasdaq.com/api/quote/{query}/info?assetclass=stocks");

            if (info?.Data?.PrimaryData is not { } pd)
                return default;
            
            var priceStr = pd.LastSalePrice;

            return new()
            {
                Name = info.Data.CompanyName,
                Symbol = sum.Data.Symbol,
                Price = double.Parse(priceStr?.Substring(1) ?? "0", NumberStyles.Any, CultureInfo.InvariantCulture),
                Close = closePrice,
                MarketCap = sd.MarketCap.Value,
                DailyVolume =
                    (long)double.Parse(sd.AverageVolume.Value ?? "0", NumberStyles.Any, CultureInfo.InvariantCulture),
            };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error getting stock data: {ErrorMessage}", ex.ToString());
            return default;
        }
    }

    private async Task<NasdaqDataResponse<T>?> GetNasdaqDataResponse<T>(string url)
    {
        using var httpClient = _httpClientFactory.CreateClient("google:search");

        var req = new HttpRequestMessage(HttpMethod.Get,
            url)
        {
            Headers =
            {
                { "Host", "api.nasdaq.com" },
                { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:132.0) Gecko/20100101 Firefox/132.0" },
                { "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8" },
                { "Accept-Language", "en-US,en;q=0.5" },
                { "Accept-Encoding", "gzip, deflate, br, zstd" },
                { "Connection", "keep-alive" },
                { "Upgrade-Insecure-Requests", "1" },
                { "Sec-Fetch-Dest", "document" },
                { "Sec-Fetch-Mode", "navigate" },
                { "Sec-Fetch-Site", "none" },
                { "Sec-Fetch-User", "?1" },
                { "Priority", "u=0, i" },
                { "TE", "trailers" }
            }
        };
        var res = await httpClient.SendAsync(req);

        var info = await res.Content.ReadFromJsonAsync<NasdaqDataResponse<T>>();
        return info;
    }

    public async Task<IReadOnlyCollection<SymbolData>> SearchSymbolAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentNullException(nameof(query));

        query = Uri.EscapeDataString(query);

        using var http = _httpClientFactory.CreateClient();

        var res = await http.GetStringAsync(
            "https://finance.yahoo.com/_finance_doubledown/api/resource/searchassist"
            + $";searchTerm={query}"
            + "?device=console");

        var data = JsonSerializer.Deserialize<YahooFinanceSearchResponse>(res);

        if (data is null or { Items: null })
            return Array.Empty<SymbolData>();

        return data.Items
                   .Where(x => x.Type == "S")
                   .Select(x => new SymbolData(x.Symbol, x.Name))
                   .ToList();
    }

    private static TypedKey<IReadOnlyCollection<CandleData>> GetCandleDataKey(string query)
        => new($"candledata:{query}");

    public async Task<IReadOnlyCollection<CandleData>> GetCandleDataAsync(string query)
        => await _cache.GetOrAddAsync(GetCandleDataKey(query),
               async () => await GetCandleDataInternalAsync(query),
               expiry: TimeSpan.FromHours(4))
           ?? [];

    public async Task<IReadOnlyCollection<CandleData>> GetCandleDataInternalAsync(string query)
    {
        using var http = _httpClientFactory.CreateClient();

        var now = DateTime.UtcNow;
        var fromdate = now.Subtract(30.Days()).ToString("yyyy-MM-dd");
        var todate = now.ToString("yyyy-MM-dd");

        var res = await GetNasdaqDataResponse<NasdaqChartResponse>(
            $"https://api.nasdaq.com/api/quote/{query}/chart?assetclass=stocks"
            + $"&fromdate={fromdate}"
            + $"&todate={todate}");

        if (res?.Data?.Chart is not { } chart)
            return Array.Empty<CandleData>();


        return chart.Select(d => new CandleData(d.Z.Open,
                        d.Z.Close,
                        d.Z.High,
                        d.Z.Low,
                        (long)double.Parse(d.Z.Volume, NumberStyles.Any, CultureInfo.InvariantCulture)))
                    .ToList();
    }
}