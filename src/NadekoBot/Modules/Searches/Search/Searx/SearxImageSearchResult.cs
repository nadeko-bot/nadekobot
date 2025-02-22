using System.Globalization;
using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches;

public sealed class SearxImageSearchResult : IImageSearchResult
{
    public string SearchTime { get; set; } = null!;

    public ISearchResultInformation Info
        => new SearxSearchResultInformation()
        {
            SearchTime = SearchTime,
            TotalResults = NumberOfResults.ToString("N", CultureInfo.InvariantCulture)
        };

    public IReadOnlyCollection<IImageSearchResultEntry> Entries
        => Results;

    [JsonPropertyName("results")]
    public List<SearxImageSearchResultEntry> Results { get; set; } = new List<SearxImageSearchResultEntry>();

    [JsonPropertyName("query")]
    public string Query { get; set; } = null!;

    [JsonPropertyName("number_of_results")]
    public double NumberOfResults { get; set; }
}