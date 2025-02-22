namespace NadekoBot.Modules.Music;

public sealed class SimpleTrackInfo : ITrackInfo
{
    public string Title { get; }
    public string Url { get; }
    public string Thumbnail { get; }
    public TimeSpan Duration { get; }
    public MusicPlatform Platform { get; }
    public string? StreamUrl { get; }

    public SimpleTrackInfo(
        string title,
        string url,
        string thumbnail,
        TimeSpan duration,
        MusicPlatform platform,
        string streamUrl)
    {
        Title = title;
        Url = url;
        Thumbnail = thumbnail;
        Duration = duration;
        Platform = platform;
        StreamUrl = streamUrl;
    }
}