namespace NadekoBot.Modules.Music;

public sealed class InvTrackInfo : ITrackInfo
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Url { get; init; }
    public required string Thumbnail { get; init; }
    public required TimeSpan Duration { get; init; }
    public required MusicPlatform Platform { get; init; }
    public required string? StreamUrl { get; init; }
}