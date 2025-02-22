using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches;

public class BibleVerse
{
    [JsonPropertyName("book_name")]
    public required string BookName { get; set; }

    public required int Chapter { get; set; }
    public required int Verse { get; set; }
    public required string Text { get; set; }
}