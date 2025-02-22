#nullable disable
using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches.Common;

public class AnimeResult
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("airing_status")]
    public string AiringStatusParsed { get; set; }

    [JsonPropertyName("title_english")]
    public string TitleEnglish { get; set; }

    [JsonPropertyName("total_episodes")]
    public int TotalEpisodes { get; set; }
    
    [JsonPropertyName("description")]
    public string Description { get; set; }

    [JsonPropertyName("image_url_lge")]
    public string ImageUrlLarge { get; set; }

    [JsonPropertyName("genres")]
    public string[] Genres { get; set; }

    [JsonPropertyName("average_score")]
    public float AverageScore { get; set; }

    
    public string AiringStatus
        => AiringStatusParsed.ToTitleCase();
    
    public string Link
        => "http://anilist.co/anime/" + Id;

    public string Synopsis
        => Description?[..(Description.Length > 500 ? 500 : Description.Length)] + "...";
}