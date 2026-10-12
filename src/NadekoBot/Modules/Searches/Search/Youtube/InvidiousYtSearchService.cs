using NadekoBot.Modules.Searches.Youtube;
using System.Buffers;
using System.Text.Json;

namespace NadekoBot.Modules.Searches;

public sealed class InvidiousYtSearchService : IYoutubeSearchService, INService
{
    private const int MAX_RESPONSE_BYTES = 1024 * 1024;
    private const int MAX_RESULTS = 5;

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
                  + $"?q={Uri.EscapeDataString(query)}"
                  + $"&type=video";
        
        using var http = _http.CreateClient(ExternalHttp.OWNER_CLIENT_NAME);
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode
            || await ExternalHttp.ReadLimitedAsync(response.Content, MAX_RESPONSE_BYTES) is not { } body)
            return null;

        List<InvidiousSearchResponse>? res;
        try
        {
            res = JsonSerializer.Deserialize<List<InvidiousSearchResponse>>(body.Buffer.AsSpan(0, body.Length),
                JsonSerializerOptions.Web);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(body.Buffer);
        }

        if (res is null or { Count: 0 })
            return null;

        var count = Math.Min(res.Count, MAX_RESULTS);
        var videos = new VideoInfo[count];
        for (var i = 0; i < count; i++)
            videos[i] = new VideoInfo(res[i].VideoId);

        return videos;
    }
}