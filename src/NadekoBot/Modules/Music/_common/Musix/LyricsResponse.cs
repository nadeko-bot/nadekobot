using System.Text.Json.Serialization;

namespace Musix.Models;

public class LyricsResponse
{
    [JsonPropertyName("lyrics")]
    public Lyrics Lyrics { get; set; } = null!;
}