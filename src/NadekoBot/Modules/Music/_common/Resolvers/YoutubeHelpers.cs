using System.Text.RegularExpressions;

namespace NadekoBot.Modules.Music;

public sealed class YoutubeHelpers
{
    public static Regex YtVideoIdRegex { get; } =
        new(@"(?:youtube\.com\/\S*(?:(?:\/e(?:mbed))?\/|watch\?(?:\S*?&?v\=))|youtu\.be\/)(?<id>[a-zA-Z0-9_-]{6,11})",
            RegexOptions.Compiled);
}