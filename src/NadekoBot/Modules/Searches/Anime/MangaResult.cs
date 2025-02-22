#nullable disable
using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches.Common;

public class MangaResult
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("publishing_status")]
    public string PublishingStatus { get; set; }

    [JsonPropertyName("image_url_lge")]
    public string ImageUrlLge { get; set; }

    [JsonPropertyName("title_english")]
    public string TitleEnglish { get; set; }

    [JsonPropertyName("total_chapters")]
    public int TotalChapters { get; set; }

    [JsonPropertyName("total_volumes")]
    public int TotalVolumes { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; }
    
    [JsonPropertyName("genres")]
    public string[] Genres { get; set; }

    [JsonPropertyName("average_score")]
    public float AverageScore { get; set; }

    public string Link
        => "http://anilist.co/manga/" + Id;

    public string Synopsis
        => Description?[..(Description.Length > 500 ? 500 : Description.Length)] + "...";
}