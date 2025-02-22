namespace NadekoBot.Modules.Music;

public interface ITrackInfo
{
    public string Id => string.Empty;
    public string Title { get; }
    public string Url { get; }
    public string Thumbnail { get; }
    public TimeSpan Duration { get; }
    public MusicPlatform Platform { get; }
}