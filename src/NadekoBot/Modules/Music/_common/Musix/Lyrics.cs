using System.Text.Json.Serialization;

namespace Musix.Models;

public class Lyrics
{
    [JsonPropertyName("lyrics_body")]
    public string LyricsBody { get; set; } = string.Empty;
}