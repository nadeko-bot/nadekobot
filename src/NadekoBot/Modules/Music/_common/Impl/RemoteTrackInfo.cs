namespace NadekoBot.Modules.Music;

public sealed record RemoteTrackInfo(
    string Id,
    string Title,
    string Url,
    string Thumbnail,
    TimeSpan Duration,
    MusicPlatform Platform) : ITrackInfo;