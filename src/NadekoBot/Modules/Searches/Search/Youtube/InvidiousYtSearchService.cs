using NadekoBot.Modules.Searches.Youtube;
using System.Net.Http.Json;

namespace NadekoBot.Modules.Searches;

public sealed class InvidiousYtSearchService : IYoutubeSearchService, INService
{
    private readonly IHttpClientFactory _http;
    private readonly SearchesConfigService _scs;
    private readonly NadekoRandom _rng;

    public InvidiousYtSearchService(
        IHttpClientFactory http,
        SearchesConfigService scs)
    {
        _http = http;
        _scs = scs;
        _rng = new();
    }

    public async Task<VideoInfo[]?> SearchAsync(string query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var instances = _scs.Data.InvidiousInstances;
        if (instances is null or { Count: 0 })
        {
            Log.Warning("Attempted to use Invidious as the .youtube provider but there are no 'invidiousInstances' "
                        + "specified in `data/searches.yml`");
            return null;
        }

        var instance = instances[_rng.Next(0, instances.Count)];

        var url = $"{instance}/api/v1/search"
                  + $"?q={query}"
                  + $"&type=video";
        
        using var http = _http.CreateClient();
        var res = await http.GetFromJsonAsync<List<InvidiousSearchResponse>>(
            url);

        if (res is null or { Count: 0 })
            return null;

        return res.Map(r => new VideoInfo(r.VideoId));
    }
}