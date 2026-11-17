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
        var effectiveMax = EffectiveMaxInternal(maxLength);
        while (!text.IsEmpty)
            results.Add(NextInternal(ref text, effectiveMax).ToString());
    }

    // The number of parts which Split makes, without the strings.
    public static int Count(ReadOnlySpan<char> text, int maxLength)
    {
        var effectiveMax = EffectiveMaxInternal(maxLength);
        var count = 0;
        while (!text.IsEmpty)
        {
            NextInternal(ref text, effectiveMax);
            count++;
        }

        return count;
    }

    private static int EffectiveMaxInternal(int maxLength)
        => maxLength - BUFFER > 0 ? maxLength - BUFFER : maxLength;

    private static ReadOnlySpan<char> NextInternal(ref ReadOnlySpan<char> text, int effectiveMax)
    {
        if (text.Length <= effectiveMax)
        {
            var last = text;
            text = default;
            return last;
        }

        var splitIndex = text[..effectiveMax].LastIndexOfAny(_whitespace);
        if (splitIndex <= 0)
            splitIndex = effectiveMax;

        var chunk = text[..splitIndex];
        text = text[splitIndex..].TrimStart(' ');
        return chunk;
    }
}
