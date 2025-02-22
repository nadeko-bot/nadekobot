using NadekoBot.Modules.Searches;
using System.Text.Json.Serialization;

namespace NadekoBot.Services;

public sealed class GoogleImageResult : IImageSearchResult
{
    ISearchResultInformation IImageSearchResult.Info
        => Info;

    IReadOnlyCollection<IImageSearchResultEntry> IImageSearchResult.Entries
        => Entries ?? Array.Empty<GoogleImageResultEntry>();

    [JsonPropertyName("searchInformation")]
    public GoogleSearchResultInformation Info { get; init; } = null!;

    [JsonPropertyName("items")]
    public IReadOnlyCollection<GoogleImageResultEntry>? Entries { get; init; }
}