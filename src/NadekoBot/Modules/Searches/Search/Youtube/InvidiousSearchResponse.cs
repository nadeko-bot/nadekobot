using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches;

public sealed class InvidiousSearchResponse
{
    [JsonPropertyName("videoId")]
    public required string VideoId { get; init; } 
    
    [JsonPropertyName("title")]
    public required string Title { get; init; }
    
    [JsonPropertyName("videoThumbnails")]
    public required List<InvidiousThumbnail> Thumbnails { get; init; }
    
    [JsonPropertyName("lengthSeconds")]
    public required int LengthSeconds { get; init; }
    
    [JsonPropertyName("description")]
    public required string Description { get; init; }
}

public sealed class InvidiousVideoResponse
{
    [JsonPropertyName("title")]
    public required string Title { get; init; }
    
    [JsonPropertyName("videoId")]
    public required string VideoId { get; init; }
    
    [JsonPropertyName("lengthSeconds")]
    public required int LengthSeconds { get; init; }

    [JsonPropertyName("videoThumbnails")]
    public required List<InvidiousThumbnail> Thumbnails { get; init; }
    
    [JsonPropertyName("adaptiveFormats")]
    public required List<InvidiousAdaptiveFormat> AdaptiveFormats { get; init; }
}

public sealed class InvidiousAdaptiveFormat
{
    [JsonPropertyName("url")]
    public required string Url { get; init; }
    
    [JsonPropertyName("audioQuality")]
    public string? AudioQuality { get; init; }
}

public sealed class InvidiousPlaylistResponse
{
    [JsonPropertyName("title")]
    public required string Title { get; init; }
    
    [JsonPropertyName("videos")]
    public required List<InvidiousVideoResponse> Videos { get; init; }
}

public sealed class InvidiousThumbnail
{
    [JsonPropertyName("url")]
    public required string Url { get; init; }
}