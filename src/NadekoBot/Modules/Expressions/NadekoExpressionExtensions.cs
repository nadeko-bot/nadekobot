#nullable disable
using NadekoBot.Db.Models;
using System.Runtime.CompilerServices;

namespace NadekoBot.Modules.NadekoExpressions;

public static class NadekoExpressionExtensions
{


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static WordPosition GetWordPosition(this ReadOnlySpan<char> str, in ReadOnlySpan<char> word)
    {
        var wordIndex = str.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        if (wordIndex == -1)
            return WordPosition.None;

        if (wordIndex == 0)
        {
            if (word.Length < str.Length && str.IsValidWordDivider(word.Length))
                return WordPosition.Start;
        }
        else if (wordIndex + word.Length == str.Length)
        {
            if (str.IsValidWordDivider(wordIndex - 1))
                return WordPosition.End;
        }
        else if (str.IsValidWordDivider(wordIndex - 1) && str.IsValidWordDivider(wordIndex + word.Length))
            return WordPosition.Middle;

        return WordPosition.None;
    }

    private static bool IsValidWordDivider(this in ReadOnlySpan<char> str, int index)
    {
        var ch = str[index];
        if (ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '1' and <= '9')
            return false;

        return true;
    }
}

public enum WordPosition
{
    None,
    Start,
    Middle,
    End
}