using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches;

public sealed class SearxSearchAttribute
{
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("entity")]
    public string? Entity { get; set; }
}