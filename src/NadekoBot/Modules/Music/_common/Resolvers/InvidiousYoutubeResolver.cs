using NadekoBot.Modules.Searches;
using System.Net.Http.Json;

namespace NadekoBot.Modules.Music;

public sealed class InvidiousYoutubeResolver : IYoutubeResolver
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly SearchesConfigService _sc;
    private readonly NadekoRandom _rng;

    private string InvidiousApiUrl
        => _sc.Data.InvidiousInstances[_rng.Next(0, _sc.Data.InvidiousInstances.Count)];

    public InvidiousYoutubeResolver(IHttpClientFactory httpFactory, SearchesConfigService sc)
    {
        _rng = new NadekoRandom();
        _httpFactory = httpFactory;
        _sc = sc;
    }

    public async Task<ITrackInfo?> ResolveByQueryAsync(string query)
    {
        using var http = _httpFactory.CreateClient();

        var items = await http.GetFromJsonAsync<List<InvidiousSearchResponse>>(
            $"{InvidiousApiUrl}/api/v1/search"
            + $"?q={query}"
            + $"&type=video");

        if (items is null || items.Count == 0)
            return null;


        var res = items.First();
        
        return new InvTrackInfo()
        {
            Id = res.VideoId,
            Title = res.Title,
            Url = $"https://youtube.com/watch?v={res.VideoId}",
            Thumbnail = res.Thumbnails?.Select(x => x.Url).FirstOrDefault() ?? string.Empty,
            Duration = TimeSpan.FromSeconds(res.LengthSeconds),
            Platform = MusicPlatform.Youtube,
            StreamUrl = null,
        };
    }

    public async Task<ITrackInfo?> ResolveByIdAsync(string id)
        => await InternalResolveByIdAsync(id);
    
    private async Task<InvTrackInfo?> InternalResolveByIdAsync(string id)
    {
        using var http = _httpFactory.CreateClient();

        var res = await http.GetFromJsonAsync<InvidiousVideoResponse>(
            $"{InvidiousApiUrl}/api/v1/videos/{id}");

        if (res is null)
            return null;

        return new InvTrackInfo()
        {
            Id = res.VideoId,
            Title = res.Title,
            Url = $"https://youtube.com/watch?v={res.VideoId}",
            Thumbnail = res.Thumbnails?.Select(x => x.Url).FirstOrDefault() ?? string.Empty,
            Duration = TimeSpan.FromSeconds(res.LengthSeconds),
            Platform = MusicPlatform.Youtube,
            StreamUrl = GetBestOpusStreamUrl(res.AdaptiveFormats)
        };
    }

    private static string? GetBestOpusStreamUrl(List<InvidiousAdaptiveFormat> formats)
    {
        // prefer Opus (WebM) for direct passthrough compatibility
        var opusFormats = formats
            .Where(f => f.AudioQuality is not null
                        && f.Type is not null
                        && f.Type.Contains("opus", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (opusFormats.Count > 0)
        {
            return opusFormats.FirstOrDefault(x => x.AudioQuality == "AUDIO_QUALITY_HIGH")?.Url
                   ?? opusFormats.FirstOrDefault(x => x.AudioQuality == "AUDIO_QUALITY_MEDIUM")?.Url
                   ?? opusFormats.FirstOrDefault(x => x.AudioQuality == "AUDIO_QUALITY_LOW")?.Url;
        }

        // fallback to any audio format
        return formats.FirstOrDefault(x => x.AudioQuality == "AUDIO_QUALITY_HIGH")?.Url
               ?? formats.FirstOrDefault(x => x.AudioQuality == "AUDIO_QUALITY_MEDIUM")?.Url
               ?? formats.FirstOrDefault(x => x.AudioQuality == "AUDIO_QUALITY_LOW")?.Url;
    }

    public async IAsyncEnumerable<ITrackInfo> ResolveTracksFromPlaylistAsync(string query)
    {
        using var http = _httpFactory.CreateClient();
        var res = await http.GetFromJsonAsync<InvidiousPlaylistResponse>(
            $"{InvidiousApiUrl}/api/v1/search?type=video&q={query}");

        if (res is null)
            yield break;

        foreach (var video in res.Videos)
        {
            yield return new InvTrackInfo()
            {
                Id = video.VideoId,
                Title = video.Title,
                Url = $"https://youtube.com/watch?v={video.VideoId}",
                Thumbnail = video.Thumbnails?.Select(x => x.Url).FirstOrDefault() ?? string.Empty,
                Duration = TimeSpan.FromSeconds(video.LengthSeconds),
                Platform = MusicPlatform.Youtube,
                StreamUrl = null
            };
        }
    }

    public Task<ITrackInfo?> ResolveByQueryAsync(string query, bool tryExtractingId)
        => ResolveByQueryAsync(query);

    public async Task<string?> GetStreamUrl(string videoId)
    {
        var video = await InternalResolveByIdAsync(videoId);
        return video?.StreamUrl;
    }
}