using System.Globalization;

namespace NadekoBot.Modules.NadekoExpressions;

public static class NadekoExpressionExtensions
{
    // Finds the first place where the word stands alone, so "cat" is found in "cats and a cat" but not in "котик" for "кот".
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
            if ((start == 0 || IsWordBreak(str[start - 1], str[start]))
                && (end == str.Length || IsWordBreak(str[end], str[end - 1])))
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
            // a letter right after the trigger belongs to the target in scripts without spaces
            WordPosition.Start => IsWordChar(content[trigger.Length])
                ? trigger.Length
                : trigger.Length + 1,
            WordPosition.End => content.Length,
            WordPosition.Middle => index + trigger.Length,
            _ => trigger.Length
        };
    }

    // outside is the character next to the word, inside is the word's own character on that side
    private static bool IsWordBreak(char outside, char inside)
        => !IsWordChar(outside) || IsUnspacedScript(outside) || IsUnspacedScript(inside);

    // Combining marks keep a decomposed accent and Thai or Hindi vowel signs inside the word.
    private static bool IsWordChar(char ch)
        => char.IsLetterOrDigit(ch)
           || char.GetUnicodeCategory(ch) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;

    // Scripts which do not put spaces between words, so a trigger in them can match inside text.
    private static bool IsUnspacedScript(char ch)
        => ch is >= '\u0E00' and <= '\u0EFF'
            or >= '\u1000' and <= '\u109F'
            or >= '\u1780' and <= '\u17FF'
            or >= '\u3005' and <= '\u3007'
            or >= '\u3040' and <= '\u30FF'
            or >= '\u31F0' and <= '\u31FF'
            or >= '\u3400' and <= '\u4DBF'
            or >= '\u4E00' and <= '\u9FFF'
            or >= '\uF900' and <= '\uFAFF'
            or >= '\uFF66' and <= '\uFF9F';
}

public enum WordPosition
{
    None,
    Start,
    Middle,
    End
}
