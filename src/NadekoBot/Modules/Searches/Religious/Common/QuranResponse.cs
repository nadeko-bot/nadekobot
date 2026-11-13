using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches;

public sealed class QuranResponse<T>
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("data")]
    public T[]? Data { get; set; }
}

public sealed record QuranVerse(int? Number, string Arabic, string English, string? AudioUrl);
