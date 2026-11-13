using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches;

public sealed class BibleVerse
{
    [JsonPropertyName("book_name")]
    public string? BookName { get; set; }

    public int? Chapter { get; set; }
    public int? Verse { get; set; }
    public string? Text { get; set; }
}
