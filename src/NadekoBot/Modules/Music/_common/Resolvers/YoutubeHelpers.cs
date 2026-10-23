using System.Buffers;
using System.Text.RegularExpressions;

namespace NadekoBot.Modules.Music;

public sealed class YoutubeHelpers
{
    private const int VIDEO_ID_LENGTH = 11;

    private static readonly SearchValues<char> _videoIdChars =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-");

    public static bool IsVideoId(ReadOnlySpan<char> id)
        => id.Length == VIDEO_ID_LENGTH && !id.ContainsAnyExcept(_videoIdChars);

    public static Regex YtVideoIdRegex { get; } =
        new(@"(?:youtube\.com\/\S*(?:(?:\/e(?:mbed))?\/|watch\?(?:\S*?&?v\=))|youtu\.be\/)(?<id>[a-zA-Z0-9_-]{6,11})",
            RegexOptions.Compiled);
}