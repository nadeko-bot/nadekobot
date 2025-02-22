#nullable disable
using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches;

public class YahooFinanceSearchResponse
{
    [JsonPropertyName("suggestionTitleAccessor")]
    public string SuggestionTitleAccessor { get; set; }

    [JsonPropertyName("suggestionMeta")]
    public List<string> SuggestionMeta { get; set; }

    [JsonPropertyName("hiConf")]
    public bool HiConf { get; set; }

    [JsonPropertyName("items")]
    public List<YahooFinanceSearchResponseItem> Items { get; set; }
}