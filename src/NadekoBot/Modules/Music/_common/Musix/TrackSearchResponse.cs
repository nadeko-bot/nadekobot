using System.Text.Json.Serialization;

namespace Musix.Models;

public class TrackSearchResponse
{
    [JsonPropertyName("track_list")]
    public List<TrackListItem> TrackList { get; set; } = new();
}