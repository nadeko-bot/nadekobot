using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using MorseCode.ITask;

namespace NadekoBot.Modules.Searches.Brave;

public sealed class BraveSearchService : SearchServiceBase, INService
{
    private readonly IHttpClientFactory _http;
    private readonly IBotCredsProvider _creds;

    public BraveSearchService(IHttpClientFactory http, IBotCredsProvider creds)
        => (_http, _creds) = (http, creds);

    private string GetApiKey()
    {
        var key = _creds.GetCreds().BraveSearchApiKey;

        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("BraveSearchApiKey is not set in creds.yml");

        return key;
    }

    public override async ITask<BraveWebSearchResult?> SearchAsync(string? query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var apiKey = GetApiKey();
        var startTime = Stopwatch.GetTimestamp();

        using var http = _http.CreateClient("brave");
        using var msg = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.search.brave.com/res/v1/web/search"
            + $"?q={Uri.EscapeDataString(query)}"
            + $"&safesearch=strict");

        msg.Headers.Add("Accept", "application/json");
        msg.Headers.Add("X-Subscription-Token", apiKey);

        using var response = await http.SendAsync(msg);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        var result = await JsonSerializer.DeserializeAsync<BraveWebSearchResponse>(stream);

        var elapsed = Stopwatch.GetElapsedTime(startTime);

        if (result?.Web?.Results is null or { Count: 0 })
            return null;

        return new BraveWebSearchResult
        {
            Answer = null,
            Entries = result.Web.Results,
            Info = new BraveSearchResultInfo
            {
                TotalResults = "?",
                SearchTime = elapsed.TotalSeconds.ToString("N2", CultureInfo.InvariantCulture)
            }
        };
    }

    public override async ITask<BraveImageSearchResult?> SearchImagesAsync(string query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var apiKey = GetApiKey();
        var startTime = Stopwatch.GetTimestamp();

        using var http = _http.CreateClient("brave");
        using var msg = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.search.brave.com/res/v1/images/search"
            + $"?q={Uri.EscapeDataString(query)}"
            + $"&safesearch=strict");

        msg.Headers.Add("Accept", "application/json");
        msg.Headers.Add("X-Subscription-Token", apiKey);

        using var response = await http.SendAsync(msg);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        var result = await JsonSerializer.DeserializeAsync<BraveImageSearchResponse>(stream);

        var elapsed = Stopwatch.GetElapsedTime(startTime);

        if (result?.Results is null or { Count: 0 })
            return null;

        return new BraveImageSearchResult
        {
            Entries = result.Results,
            Info = new BraveSearchResultInfo
            {
                TotalResults = "?",
                SearchTime = elapsed.TotalSeconds.ToString("N2", CultureInfo.InvariantCulture)
            }
        };
    }
}
