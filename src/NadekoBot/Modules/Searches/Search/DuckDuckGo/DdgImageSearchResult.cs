using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches.DuckDuckGo;

public sealed class DdgImageApiResponse
{
    [JsonPropertyName("results")]
    public List<DdgImageSearchResultEntry> Results { get; set; } = [];
}

public sealed class DdgImageSearchResultEntry : IImageSearchResultEntry
{
    [JsonIgnore]
    public string Link => Image ?? string.Empty;

    [JsonPropertyName("image")]
    public string? Image { get; set; }

    [JsonPropertyName("thumbnail")]
    public string? Thumbnail { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public sealed class DdgImageSearchResult : IImageSearchResult
{
    public required ISearchResultInformation Info { get; init; }
    public required IReadOnlyCollection<IImageSearchResultEntry> Entries { get; init; }
}
