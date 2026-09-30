using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches;

public sealed class XkcdService(IHttpClientFactory httpFactory) : INService
{
    public const string XKCD_URL = "https://xkcd.com";

    // xkcd skips 404 as a joke, the url returns a real 404
    private const int MISSING_COMIC = 404;

    // used for random comics when xkcd.com can not be reached and no number is known yet
    private const int KNOWN_LATEST_COMIC = 3302;

    private static readonly TimeSpan _latestTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan _retryInterval = TimeSpan.FromMinutes(5);

    private volatile LatestEntry? _latest;

    private sealed record LatestEntry(int Num, long ExpiresAt);

    public Task<XkcdComic?> GetComicAsync(int num)
    {
        if (num < 1 || num == MISSING_COMIC)
            return Task.FromResult<XkcdComic?>(null);

        return FetchInternalAsync($"{XKCD_URL}/{num}/info.0.json");
    }

    public async Task<XkcdComic?> GetLatestAsync()
    {
        var comic = await FetchInternalAsync($"{XKCD_URL}/info.0.json");
        if (comic is not null)
            _latest = new(comic.Num, Environment.TickCount64 + (long)_latestTtl.TotalMilliseconds);

        return comic;
    }

    public async Task<XkcdComic?> GetRandomAsync()
    {
        var latest = await GetLatestNumInternalAsync();

        // pick from one fewer number and step over the missing comic, so every comic has the same chance
        var num = Random.Shared.Next(1, latest);
        if (num >= MISSING_COMIC)
            num++;

        return await GetComicAsync(num);
    }

    private async Task<int> GetLatestNumInternalAsync()
    {
        var cached = _latest;
        if (cached is not null && Environment.TickCount64 < cached.ExpiresAt)
            return cached.Num;

        var comic = await GetLatestAsync();
        if (comic is not null)
            return comic.Num;

        var fallback = Math.Max(cached?.Num ?? 0, KNOWN_LATEST_COMIC);
        _latest = new(fallback, Environment.TickCount64 + (long)_retryInterval.TotalMilliseconds);
        return fallback;
    }

    private async Task<XkcdComic?> FetchInternalAsync(string url)
    {
        using var http = httpFactory.CreateClient();
        try
        {
            return await http.GetFromJsonAsync<XkcdComic>(url);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        {
            Log.Warning(ex, "Unable to fetch xkcd comic from {Url}", url);
            return null;
        }
    }
}

public sealed class XkcdComic
{
    [JsonPropertyName("num")]
    public int Num { get; set; }

    [JsonPropertyName("day")]
    public string Day { get; set; } = "";

    [JsonPropertyName("month")]
    public string Month { get; set; } = "";

    [JsonPropertyName("year")]
    public string Year { get; set; } = "";

    [JsonPropertyName("safe_title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("img")]
    public string ImageLink { get; set; } = "";

    [JsonPropertyName("alt")]
    public string Alt { get; set; } = "";
}
