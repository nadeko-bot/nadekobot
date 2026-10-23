using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches.Common;

public sealed class OmdbMovie
{
    [JsonPropertyName("Title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("Year")]
    public string? Year { get; set; }

    [JsonPropertyName("imdbRating")]
    public string? ImdbRating { get; set; }

    [JsonPropertyName("imdbID")]
    public string? ImdbId { get; set; }

    [JsonPropertyName("Genre")]
    public string? Genre { get; set; }

    [JsonPropertyName("Plot")]
    public string? Plot { get; set; }

    [JsonPropertyName("Poster")]
    public string? Poster { get; set; }

    [JsonPropertyName("Error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }
}
