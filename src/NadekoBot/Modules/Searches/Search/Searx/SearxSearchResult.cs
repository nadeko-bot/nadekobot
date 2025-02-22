using System.Globalization;
using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches;

public sealed class SearxSearchResult : ISearchResult
{
    [JsonPropertyName("query")]
    public string Query { get; set; } = null!;

    [JsonPropertyName("number_of_results")]
    public double NumberOfResults { get; set; }

    [JsonPropertyName("results")]
    public List<SearxSearchResultEntry> Results { get; set; } = new List<SearxSearchResultEntry>();

    [JsonPropertyName("answers")]
    public List<string> Answers { get; set; } = new List<string>();
    //
    // [JsonPropertyName("corrections")]
    // public List<object> Corrections { get; } = new List<object>();

    // [JsonPropertyName("infoboxes")]
    // public List<InfoboxModel> Infoboxes { get; } = new List<InfoboxModel>();
    //
    // [JsonPropertyName("suggestions")]
    // public List<string> Suggestions { get; } = new List<string>();

    // [JsonPropertyName("unresponsive_engines")]
    // public List<object> UnresponsiveEngines { get; } = new List<object>();


    public string SearchTime { get; set; } = null!;

    public IReadOnlyCollection<ISearchResultEntry> Entries
        => Results;

    public ISearchResultInformation Info
        => new SearxSearchResultInformation()
        {
            SearchTime = SearchTime,
            TotalResults = NumberOfResults.ToString("N", CultureInfo.InvariantCulture)
        };

    public string? Answer
        => Answers.FirstOrDefault();
}