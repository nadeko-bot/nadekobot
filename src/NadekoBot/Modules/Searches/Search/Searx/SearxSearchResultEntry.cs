using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches;

public sealed class SearxSearchResultEntry : ISearchResultEntry
{
    public string DisplayUrl
        => Url;

    public string Description
        => Content.TrimTo(768)!;

    [JsonPropertyName("url")]
    public string Url { get; set; } = null!;

    [JsonPropertyName("title")]
    public string Title { get; set; } = null!;

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    // [JsonPropertyName("engine")]
    // public string Engine { get; set; }
    //
    // [JsonPropertyName("parsed_url")]
    // public List<string> ParsedUrl { get; } = new List<string>();
    //
    // [JsonPropertyName("template")]
    // public string Template { get; set; }
    //
    // [JsonPropertyName("engines")]
    // public List<string> Engines { get; } = new List<string>();
    //
    // [JsonPropertyName("positions")]
    // public List<int> Positions { get; } = new List<int>();
    //
    // [JsonPropertyName("score")]
    // public double Score { get; set; }
    //
    // [JsonPropertyName("category")]
    // public string Category { get; set; }
    //
    // [JsonPropertyName("pretty_url")]
    // public string PrettyUrl { get; set; }
    //
    // [JsonPropertyName("open_group")]
    // public bool OpenGroup { get; set; }
    //
    // [JsonPropertyName("close_group")]
    // public bool? CloseGroup { get; set; }
}