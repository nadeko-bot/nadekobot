using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches.Brave;

public sealed class BraveImageSearchResponse
{
    [JsonPropertyName("results")]
    public List<BraveImageSearchResultEntry> Results { get; set; } = [];
}

public sealed class BraveImageSearchResultEntry : IImageSearchResultEntry
{
    [JsonIgnore]
    public string Link => Properties?.Url ?? Url ?? string.Empty;

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("properties")]
    public BraveImageProperties? Properties { get; set; }
}

public sealed class BraveImageProperties
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = null!;
}

public sealed class BraveImageSearchResult : IImageSearchResult
{
    public required ISearchResultInformation Info { get; init; }
    public required IReadOnlyCollection<IImageSearchResultEntry> Entries { get; init; }
}
