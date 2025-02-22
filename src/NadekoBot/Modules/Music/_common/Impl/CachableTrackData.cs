#nullable disable
using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Music;

public sealed class CachableTrackData : ICachableTrackData
{
    public string Title { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Thumbnail { get; set; } = string.Empty;
    public double TotalDurationMs { get; set; }

    [JsonIgnore]
    public TimeSpan Duration
        => TimeSpan.FromMilliseconds(TotalDurationMs);

    public MusicPlatform Platform { get; set; }
}