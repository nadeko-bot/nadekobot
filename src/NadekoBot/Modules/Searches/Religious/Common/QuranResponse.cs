using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches;

public sealed class QuranResponse<T>
{
    [JsonPropertyName("code")]
    public required int Code { get; set; }

    [JsonPropertyName("status")]
    public required string Status { get; set; }

    [JsonPropertyName("data")]
    public required T[] Data { get; set; }
}