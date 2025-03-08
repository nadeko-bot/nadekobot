using System.Text.Json.Serialization;

namespace Musix.Models;

public class Track
{
    [JsonPropertyName("track_id")]
    public int TrackId { get; set; }

    [JsonPropertyName("track_name")]
    public string TrackName { get; set; } = string.Empty;

    [JsonPropertyName("artist_name")]
    public string ArtistName { get; set; } = string.Empty;

    [JsonPropertyName("album_name")]
    public string AlbumName { get; set; } = string.Empty;

    [JsonPropertyName("track_share_url")]
    public string TrackShareUrl { get; set; } = string.Empty;

    public override string ToString() => $"{TrackName} by {ArtistName} (Album: {AlbumName})";
}