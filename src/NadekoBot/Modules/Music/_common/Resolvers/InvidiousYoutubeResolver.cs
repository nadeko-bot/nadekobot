using NadekoBot.Modules.Searches;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NadekoBot.Modules.Music;

public sealed class InvidiousYoutubeResolver : IYoutubeResolver
{
    private static readonly Regex _playlistIdRegex =
        new(@"[?&]list=(?<id>[\w\-]{12,})", RegexOptions.Compiled);

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
        var instance = InvidiousApiUrl;
        var items = await GetJsonInternalAsync<List<InvidiousSearchResponse>>(instance,
            $"{instance}/api/v1/search"
            + $"?q={Uri.EscapeDataString(query)}"
            + $"&type=video");

        if (items is null)
            return null;

        foreach (var item in items)
        {
            if (!string.IsNullOrWhiteSpace(item?.VideoId))
                return ToTrackInfoInternal(item.VideoId, item.Title, item.Thumbnails, item.LengthSeconds, null);
        }

        return null;
    }

    public async Task<ITrackInfo?> ResolveByIdAsync(string id)
        => await InternalResolveByIdAsync(id);
    
    private async Task<InvTrackInfo?> InternalResolveByIdAsync(string id)
    {
        var instance = InvidiousApiUrl;
        var res = await GetJsonInternalAsync<InvidiousVideoResponse>(instance, $"{instance}/api/v1/videos/{id}");
        if (res is null)
            return null;

        return ToTrackInfoInternal(string.IsNullOrWhiteSpace(res.VideoId) ? id : res.VideoId,
            res.Title,
            res.Thumbnails,
            res.LengthSeconds,
            GetBestOpusStreamUrl(res.AdaptiveFormats));
    }

    private static InvTrackInfo ToTrackInfoInternal(
        string videoId,
        string? title,
        List<InvidiousThumbnail>? thumbnails,
        int? lengthSeconds,
        string? streamUrl)
        => new()
        {
            Id = videoId,
            Title = string.IsNullOrWhiteSpace(title) ? videoId : title,
            Url = $"https://youtube.com/watch?v={videoId}",
            Thumbnail = InvidiousThumbnail.FirstUrl(thumbnails) ?? string.Empty,
            Duration = TimeSpan.FromSeconds(lengthSeconds ?? 0),
            Platform = MusicPlatform.Youtube,
            StreamUrl = streamUrl
        };

    private async Task<T?> GetJsonInternalAsync<T>(string instance, string url)
    {
        using var http = _httpFactory.CreateClient(ExternalHttp.OWNER_CLIENT_NAME);
        try
        {
            return await http.GetFromJsonAsync<T>(url);
        }
        catch (JsonException ex)
        {
            SiteAnswer.LogUnreadable(instance, ex);
            return default;
        }
    }

    private static string? GetBestOpusStreamUrl(List<InvidiousAdaptiveFormat>? formats)
    {
        if (formats is null)
            return null;

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
        var playlistId = ExtractPlaylistId(query);
        if (playlistId is null)
            yield break;

        var instance = InvidiousApiUrl;
        var res = await GetJsonInternalAsync<InvidiousPlaylistResponse>(instance,
            $"{instance}/api/v1/playlists/{Uri.EscapeDataString(playlistId)}");

        if (res?.Videos is null)
            yield break;

        foreach (var video in res.Videos)
        {
            if (string.IsNullOrWhiteSpace(video?.VideoId))
                continue;

            yield return ToTrackInfoInternal(video.VideoId, video.Title, video.Thumbnails, video.LengthSeconds, null);
        }
    }

    private static string? ExtractPlaylistId(string query)
    {
        var match = _playlistIdRegex.Match(query);
        return match.Success ? match.Groups["id"].Value : null;
    }

    public Task<ITrackInfo?> ResolveByQueryAsync(string query, bool tryExtractingId, bool allowLocalNetwork)
        => ResolveByQueryAsync(query);

    public async Task<string?> GetStreamUrl(string videoId)
    {
        var video = await InternalResolveByIdAsync(videoId);
        return video?.StreamUrl;
    }
}
