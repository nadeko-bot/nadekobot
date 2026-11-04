namespace NadekoBot.Modules.NadekoExpressions;

public static class NadekoExpressionExtensions
{
    // Finds the first place where the word stands alone, so "cat" is found in "cats and a cat".
    public static WordPosition GetWordPosition(this ReadOnlySpan<char> str, ReadOnlySpan<char> word, out int index)
    {
        index = -1;
        if (word.IsEmpty)
            return WordPosition.None;

        var offset = 0;
        while (offset <= str.Length - word.Length)
        {
            var found = str[offset..].IndexOf(word, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
                return WordPosition.None;

            var start = offset + found;
            var end = start + word.Length;
            if ((start == 0 || IsWordDivider(str[start - 1]))
                && (end == str.Length || IsWordDivider(str[end])))
            {
                if (start == 0)
                {
                    if (end == str.Length)
                        return WordPosition.None;

                    index = start;
                    return WordPosition.Start;
                }

                index = start;
                return end == str.Length ? WordPosition.End : WordPosition.Middle;
            }

            offset = start + 1;
        }

        return WordPosition.None;
    }

    // Where the %target% text starts in the message which triggered the expression.
    public static int GetTargetStart(this ReadOnlySpan<char> content, ReadOnlySpan<char> trigger, bool containsAnywhere)
    {
        if (!containsAnywhere)
            return trigger.Length;

        return content.GetWordPosition(trigger, out var index) switch
        {
            // a CJK letter right after the trigger belongs to the target
            WordPosition.Start => char.IsLetterOrDigit(content[trigger.Length])
                ? trigger.Length
                : trigger.Length + 1,
            WordPosition.End => content.Length,
            WordPosition.Middle => index + trigger.Length,
            _ => trigger.Length
        };
    }

    private static bool IsWordDivider(char ch)
        => ch is not (>= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9');
}

public enum WordPosition
{
    None,
    Start,
    Middle,
    End
}
