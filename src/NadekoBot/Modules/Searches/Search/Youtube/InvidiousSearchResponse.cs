using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches;

// Invidious instances run different versions, so each field can be missing.
public sealed class InvidiousSearchResponse
{
    [JsonPropertyName("videoId")]
    public string? VideoId { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("videoThumbnails")]
    public List<InvidiousThumbnail>? Thumbnails { get; init; }

    [JsonPropertyName("lengthSeconds")]
    public int? LengthSeconds { get; init; }
}

public sealed class InvidiousVideoResponse
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("videoId")]
    public string? VideoId { get; init; }

    [JsonPropertyName("lengthSeconds")]
    public int? LengthSeconds { get; init; }

    [JsonPropertyName("videoThumbnails")]
    public List<InvidiousThumbnail>? Thumbnails { get; init; }

    [JsonPropertyName("adaptiveFormats")]
    public List<InvidiousAdaptiveFormat>? AdaptiveFormats { get; init; }
}

public sealed class InvidiousAdaptiveFormat
{
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("audioQuality")]
    public string? AudioQuality { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }
}

public sealed class InvidiousPlaylistResponse
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("videos")]
    public List<InvidiousPlaylistVideo>? Videos { get; init; }
}

public sealed class InvidiousPlaylistVideo
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("videoId")]
    public string? VideoId { get; init; }

    [JsonPropertyName("lengthSeconds")]
    public int? LengthSeconds { get; init; }

    [JsonPropertyName("videoThumbnails")]
    public List<InvidiousThumbnail>? Thumbnails { get; init; }
}

public sealed class InvidiousThumbnail
{
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    public static string? FirstUrl(List<InvidiousThumbnail>? thumbnails, int start = 0)
    {
        if (thumbnails is null)
            return null;

        for (var i = start; i < thumbnails.Count; i++)
        {
            var url = thumbnails[i]?.Url;
            if (!string.IsNullOrWhiteSpace(url))
                return url;
        }

        return null;
    }
}
