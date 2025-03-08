using System.Text.Json.Serialization;

namespace Musix.Models;

public class TrackListItem
{
    [JsonPropertyName("track")]
    public Track Track { get; set; } = null!;
}