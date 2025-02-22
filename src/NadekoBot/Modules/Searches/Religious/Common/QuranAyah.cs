#nullable disable

using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches;

public sealed class QuranAyah
{
    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("audio")]
    public string Audio { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; }
}