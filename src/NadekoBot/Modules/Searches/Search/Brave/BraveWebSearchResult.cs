using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches.Brave;

public sealed class BraveWebSearchResponse
{
    [JsonPropertyName("web")]
    public BraveWebResults? Web { get; set; }
}

public sealed class BraveWebResults
{
    [JsonPropertyName("results")]
    public List<BraveWebSearchResultEntry> Results { get; set; } = [];
}

public sealed class BraveWebSearchResultEntry : ISearchResultEntry
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = null!;

    [JsonPropertyName("url")]
    public string Url { get; set; } = null!;

    [JsonIgnore]
    public string DisplayUrl => Url;

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public sealed class BraveWebSearchResult : ISearchResult
{
    public required string? Answer { get; init; }
    public required IReadOnlyCollection<ISearchResultEntry> Entries { get; init; }
    public required ISearchResultInformation Info { get; init; }
}

public sealed class BraveSearchResultInfo : ISearchResultInformation
{
    public string TotalResults { get; init; } = null!;
    public string SearchTime { get; init; } = null!;
}
