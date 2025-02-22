#nullable disable
namespace NadekoBot.Modules.Music;

public interface ICachableTrackData
{
    string Id { get; set; }
    string Url { get; set; }
    string Thumbnail { get; set; }
    public TimeSpan Duration { get; }
    MusicPlatform Platform { get; set; }
    string Title { get; set; }
}