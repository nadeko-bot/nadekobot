#nullable disable
using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches;

public class YahooFinanceSearchResponseItem
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("exch")]
    public string Exch { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("exchDisp")]
    public string ExchDisp { get; set; }

    [JsonPropertyName("typeDisp")]
    public string TypeDisp { get; set; }
}