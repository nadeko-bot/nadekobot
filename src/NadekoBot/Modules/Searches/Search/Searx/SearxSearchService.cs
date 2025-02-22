using MorseCode.ITask;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace NadekoBot.Modules.Searches;

public sealed class SearxSearchService : SearchServiceBase, INService
{
    private readonly IHttpClientFactory _http;
    private readonly SearchesConfigService _scs;
    
    private static readonly Random _rng = new NadekoRandom();

    public SearxSearchService(IHttpClientFactory http, SearchesConfigService scs)
        => (_http, _scs) = (http, scs);

    private string GetRandomInstance()
    {
        var instances = _scs.Data.SearxInstances;

        if (instances is null or { Count: 0 })
            throw new InvalidOperationException("No searx instances specified in searches.yml");

        return instances[_rng.Next(0, instances.Count)];
    }
    
    public override async ITask<SearxSearchResult> SearchAsync(string? query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var instanceUrl = GetRandomInstance();
        
        Log.Information("Using {Instance} instance for web search...", instanceUrl);
        var startTime = Stopwatch.GetTimestamp();
        
        using var http = _http.CreateClient();
        await using var res = await http.GetStreamAsync($"{instanceUrl}"
                                                        + $"?q={Uri.EscapeDataString(query)}"
                                                        + $"&format=json"
                                                        + $"&strict=2");

        var elapsed = Stopwatch.GetElapsedTime(startTime);
        var dat = await JsonSerializer.DeserializeAsync<SearxSearchResult>(res);

        if (dat is null)
            return new SearxSearchResult();
        
        dat.SearchTime = elapsed.TotalSeconds.ToString("N2", CultureInfo.InvariantCulture); 
        return dat;
    }

    public override async ITask<SearxImageSearchResult> SearchImagesAsync(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        
        var instanceUrl = GetRandomInstance();
        
        Log.Information("Using {Instance} instance for img search...", instanceUrl);
        var startTime = Stopwatch.GetTimestamp();
        using var http = _http.CreateClient();
        await using var res = await http.GetStreamAsync($"{instanceUrl}"
                                                        + $"?q={Uri.EscapeDataString(query)}"
                                                        + $"&format=json"
                                                        + $"&category_images=on"
                                                        + $"&strict=2");

        var elapsed = Stopwatch.GetElapsedTime(startTime);
        var dat = await JsonSerializer.DeserializeAsync<SearxImageSearchResult>(res);

        if (dat is null)
            return new SearxImageSearchResult();
        
        dat.SearchTime = elapsed.TotalSeconds.ToString("N2", CultureInfo.InvariantCulture); 
        return dat;
    }
}