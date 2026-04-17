using System.Buffers;

namespace NadekoBot.Extensions;

public static class MessageSplitter
{
    private const int BUFFER = 100;
    public const int MAX_PLAIN_TEXT_LENGTH = 2000;
    public const int MAX_EMBED_DESC_LENGTH = 4096;

    private static readonly SearchValues<char> _whitespace = SearchValues.Create(" \n\t\r");

    public static void Split(
        ReadOnlySpan<char> text,
        int maxLength,
        List<string> results)
    {
        if (text.IsEmpty)
            return;

        var effectiveMax = maxLength - BUFFER;
        if (effectiveMax <= 0)
            effectiveMax = maxLength;

        while (text.Length > effectiveMax)
        {
            var chunk = text[..effectiveMax];
            var splitIndex = chunk.LastIndexOfAny(_whitespace);

            if (splitIndex <= 0)
                splitIndex = effectiveMax;

            results.Add(text[..splitIndex].ToString());
            text = text[splitIndex..].TrimStart(' ');
        }

        if (text.Length > 0)
            results.Add(text.ToString());
    }
}
