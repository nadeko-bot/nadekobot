namespace NadekoBot.Modules.Searches.Youtube;

public readonly struct VideoInfo
{
    public VideoInfo(string videoId)
        => Url = $"https://youtube.com/watch?v={videoId}";

    public string Url { get; init; }
}