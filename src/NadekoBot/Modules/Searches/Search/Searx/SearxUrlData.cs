using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches;

public sealed class SearxUrlData
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = null!;

    [JsonPropertyName("url")]
    public string Url { get; set; } = null!;

    [JsonPropertyName("official")]
    public bool? Official { get; set; }
}