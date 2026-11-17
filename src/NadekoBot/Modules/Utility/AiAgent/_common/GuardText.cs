using System.Buffers;
using System.Globalization;
using System.Text;
using NadekoBot.Modules.Help;

namespace NadekoBot.Modules.Utility.AiAgent;

public static class GuardText
{
    private const int MIN_NAME_CHARS = 3;
    private const int MIN_WORD_TOKEN = 3;
    private const int MIN_TARGET_CHARS = 2;
    private const string TOKEN_TRIM = ",.;:!?\"'()[]{}*_~`";
    private const int MAX_STACK_CHARS = 256;

    // CJK text has no spaces between words, so its characters never join a phrase to the text around it.
    private const char FIRST_CJK = '\u2E80';

    // Plane 14 holds tag letters (ASCII from U+E0020), variation selectors and no visible sign before U+E1000.
    private const int FIRST_PLANE_14 = 0xE0000;
    private const int LAST_HIDDEN_PLANE_14 = 0xE0FFF;
    private const int FIRST_TAG_LETTER = 0xE0020;
    private const int LAST_TAG_LETTER = 0xE007E;

    // The amount readers of the bot take these words and expressions such as "all-1".
    private static readonly string[] _amountWords = ["all", "allin", "half", "max"];
    private static readonly SearchValues<char> _amountOperators = SearchValues.Create("+-*/%^");

    // Signs which show nothing but are no format characters: fillers, selectors, blank Braille and free code points of format blocks.
    private static readonly SearchValues<char> _blankSigns = SearchValues.Create(
        "\u034F\u115F\u1160\u17B4\u17B5\u180B\u180C\u180D\u180F\u2065\u2800\u3164\uFFA0\uFFF0\uFFF1\uFFF2\uFFF3\uFFF4\uFFF5\uFFF6\uFFF7\uFFF8");

    // Spaces split like the command parser, "<@!" is "<@", invisible signs go, tag letters are ASCII, and NFKC: "Ｍal\u200Blory" is "Mallory".
    public static string Normalize(ReadOnlySpan<char> text)
        => NormalizeInternal(text, Reading.Main, out _);

    public static string Normalize(ReadOnlySpan<char> text, out bool otherReading)
        => NormalizeInternal(text, Reading.Main, out otherReading);

    // The other reading of hidden signs: invisible signs as spaces (".slot\u200Bplease"), no dropped controls, no Zalgo ("Mal̶lory").
    public static string NormalizeOther(ReadOnlySpan<char> text)
        => NormalizeInternal(text, Reading.Other, out _);

    private static string NormalizeInternal(ReadOnlySpan<char> text, Reading reading, out bool otherReading)
    {
        text = text.Trim();
        otherReading = false;
        if (text.IsEmpty)
            return "";

        // Only a space after a run of tag letters adds a sign, and a run takes 2 chars or more, so the text grows by half at most.
        var size = text.Length + text.Length / 2 + 1;
        char[]? rented = null;
        Span<char> buffer = size <= MAX_STACK_CHARS ? stackalloc char[MAX_STACK_CHARS] : rented = ArrayPool<char>.Shared.Rent(size);
        try
        {
            var n = 0;
            var space = false;
            var ascii = true;
            var inTags = false;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                var tag = false;

                // A run of tag letters is a word of its own in the other reading.
                var letter = TagLetterInternal(text, i);
                if (reading == Reading.Other && inTags != letter.HasValue)
                    space = true;

                inTags = letter.HasValue;
                if (letter is { } spelled)
                {
                    otherReading = tag = true;
                    c = spelled;
                    i++;
                }
                else if (char.IsControl(c) && char.IsWhiteSpace(c) && c is not ('\t' or '\n' or '\r'))
                {
                    // The prompt drops most of these, but the command parser splits at them.
                    otherReading = true;
                    space |= reading == Reading.Main;
                    continue;
                }
                else if (InvisibleLengthInternal(text, i) is var hidden and > 0)
                {
                    otherReading = true;
                    space |= reading == Reading.Other;
                    i += hidden - 1;
                    continue;
                }
                else if (!char.IsAscii(c) && char.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark)
                {
                    otherReading = true;
                    if (reading == Reading.Other)
                        continue;
                }

                if (char.IsWhiteSpace(c))
                {
                    space = true;
                    continue;
                }

                if (space && n > 0)
                    buffer[n++] = ' ';

                space = false;
                if (!tag && c == '<' && i + 2 < text.Length && text[i + 1] == '@' && text[i + 2] == '!')
                {
                    buffer[n++] = '<';
                    buffer[n++] = '@';
                    i += 2;
                    continue;
                }

                if (char.IsSurrogate(c))
                {
                    ascii = false;
                    if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    {
                        buffer[n++] = c;
                        buffer[n++] = text[++i];
                    }
                    else
                    {
                        // Half of a pair, as at the end of a cut text, would make NFKC throw.
                        buffer[n++] = '\uFFFD';
                    }

                    continue;
                }

                ascii &= char.IsAscii(c);
                buffer[n++] = c;
            }

            var normalized = new string(buffer[..n]);
            return ascii ? normalized : normalized.Normalize(NormalizationForm.FormKC);
        }
        finally
        {
            if (rented is not null)
                ArrayPool<char>.Shared.Return(rented, clearArray: true);
        }
    }

    private static char? TagLetterInternal(ReadOnlySpan<char> text, int i)
    {
        if (!char.IsHighSurrogate(text[i]) || i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
            return null;

        var value = char.ConvertToUtf32(text[i], text[i + 1]);
        return value is >= FIRST_TAG_LETTER and <= LAST_TAG_LETTER ? (char)(value - FIRST_PLANE_14) : null;
    }

    private static int InvisibleLengthInternal(ReadOnlySpan<char> text, int i)
    {
        var c = text[i];
        if (char.IsControl(c))
            return char.IsWhiteSpace(c) ? 0 : 1;

        if (char.IsAscii(c))
            return 0;

        if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
        {
            var rune = new Rune(c, text[i + 1]);
            return rune.Value is >= FIRST_PLANE_14 and <= LAST_HIDDEN_PLANE_14 || Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format
                ? 2
                : 0;
        }

        return c is >= '\uFE00' and <= '\uFE0F' || _blankSigns.Contains(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format
            ? 1
            : 0;
    }

    private enum Reading
    {
        Main,
        Other
    }

    // A custom emoji is no word, so "<:100:1>" is no amount. Blanks keep the length of the text.
    public static string WithoutCustomEmoji(string text)
    {
        char[]? chars = null;
        var span = text.AsSpan();
        var i = span.IndexOf('<');
        while (i >= 0)
        {
            var length = CustomEmojiLengthInternal(span[i..]);
            if (length > 0)
            {
                chars ??= text.ToCharArray();
                chars.AsSpan(i, length).Fill(' ');
            }

            i += Math.Max(length, 1);
            var next = span[i..].IndexOf('<');
            i = next < 0 ? -1 : i + next;
        }

        return chars is null ? text : new string(chars);
    }

    private static int CustomEmojiLengthInternal(ReadOnlySpan<char> text)
    {
        var i = text is ['<', 'a', ':', ..] ? 3 : text is ['<', ':', ..] ? 2 : 0;
        if (i == 0)
            return 0;

        var nameStart = i;
        while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] == '_'))
            i++;

        if (i == nameStart || i >= text.Length || text[i] != ':')
            return 0;

        var idStart = ++i;
        while (i < text.Length && char.IsAsciiDigit(text[i]))
            i++;

        return i > idStart && i < text.Length && text[i] == '>' ? i + 1 : 0;
    }

    // No letter or digit may touch either end of the phrase.
    public static bool ContainsPhrase(ReadOnlySpan<char> text, ReadOnlySpan<char> phrase)
        => IndexOfPhraseInternal(text, phrase, ReadOnlySpan<char>.Empty, 0) >= 0;

    public static bool NamesCommand(ReadOnlySpan<char> text, IReadOnlyList<string> aliases, string prefix)
    {
        for (var i = 0; i < aliases.Count; i++)
        {
            var alias = aliases[i];
            if (alias.Length >= MIN_NAME_CHARS && ContainsPhrase(text, alias))
                return true;

            if ((prefix.Length > 0 && IndexOfPhraseInternal(text, alias, prefix, 0) >= 0)
                || IndexOfPhraseInternal(text, alias, CommandListGenerator.DefaultPrefix, 0) >= 0)
                return true;
        }

        return false;
    }

    public static ReadOnlySpan<char> CoreToken(ReadOnlySpan<char> token)
    {
        token = token.Trim(TOKEN_TRIM);
        if (token is ['<', .., '>']
            && token[1..^1].TrimStart("@!&#") is { IsEmpty: false } id
            && !id.ContainsAnyExceptInRange('0', '9'))
            return id;

        return token.TrimStart('@');
    }

    public static bool IsAmountOrIdToken(ReadOnlySpan<char> token)
    {
        if (token.IsEmpty)
            return false;

        if (token.ContainsAnyInRange('0', '9') || token.ContainsAny(_amountOperators))
            return true;

        foreach (var word in _amountWords)
        {
            if (token.Equals(word, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    // A CJK name can have one letter.
    public static bool IsWordToken(ReadOnlySpan<char> token)
        => token.Length >= MIN_TARGET_CHARS || HasCjkLetterInternal(token);

    public static bool CallsCommand(ReadOnlySpan<char> text, IReadOnlyList<string> aliases, string prefix)
    {
        for (var i = 0; i < aliases.Count; i++)
        {
            var alias = aliases[i];
            if ((prefix.Length > 0 && IndexOfPhraseInternal(text, alias, prefix, 0) >= 0)
                || IndexOfPhraseInternal(text, alias, CommandListGenerator.DefaultPrefix, 0) >= 0)
                return true;
        }

        return false;
    }

    // A number matches only as a whole number, also in other digits ("１００") or in groups ("1,000"), but not in "1,100".
    public static bool HoldsToken(ReadOnlySpan<char> text, ReadOnlySpan<char> token)
        => IsDigitsInternal(token)
            ? HoldsNumberInternal(text, token, false) || HoldsNumberInternal(text, token, true)
            : ContainsPhrase(text, token);

    private static bool IsDigitsInternal(ReadOnlySpan<char> token)
    {
        if (token.IsEmpty)
            return false;

        foreach (var c in token)
        {
            if (!char.IsDigit(c))
                return false;
        }

        return true;
    }

    public static List<string> ArgumentTokens(string normalizedArgs)
    {
        var numbers = new List<string>();
        var words = new List<string>();
        var span = normalizedArgs.AsSpan();
        foreach (var range in span.Split(' '))
        {
            var token = CoreToken(span[range]);
            if (token.IsEmpty)
                continue;

            if (token.ContainsAnyInRange('0', '9'))
                numbers.Add(token.ToString());
            else if (token.Length >= MIN_WORD_TOKEN)
                words.Add(token.ToString());
        }

        return numbers.Count > 0 ? numbers : words;
    }

    private static bool HasCjkLetterInternal(ReadOnlySpan<char> word)
    {
        foreach (var c in word)
        {
            if (c >= FIRST_CJK && char.IsLetter(c))
                return true;
        }

        return false;
    }

    private static bool HoldsNumberInternal(ReadOnlySpan<char> text, ReadOnlySpan<char> number, bool joinGroups)
    {
        var i = 0;
        while (i < text.Length)
        {
            if (!char.IsDigit(text[i]))
            {
                i++;
                continue;
            }

            var start = i;
            var matched = 0;
            var same = true;
            while (i < text.Length)
            {
                var c = text[i];
                if (char.IsDigit(c))
                {
                    same &= matched < number.Length
                            && CharUnicodeInfo.GetDecimalDigitValue(c) == CharUnicodeInfo.GetDecimalDigitValue(number[matched]);
                    matched++;
                    i++;
                }
                else if (joinGroups && IsDigitSeparatorInternal(c) && IsGroupOfThreeInternal(text, i + 1))
                    i++;
                else
                    break;
            }

            if (same
                && matched == number.Length
                && (start == 0 || !IsWordCharInternal(text[start - 1]))
                && (i == text.Length || !IsWordCharInternal(text[i]))
                && !(start >= 2 && IsDigitSeparatorInternal(text[start - 1]) && char.IsDigit(text[start - 2]))
                && !(i + 1 < text.Length && IsDigitSeparatorInternal(text[i]) && char.IsDigit(text[i + 1])))
                return true;
        }

        return false;
    }

    private static bool IsDigitSeparatorInternal(char c)
        => c is ',' or '.' or '\'' or '\u2019' or '\u00A0' or '\u202F';

    private static bool IsGroupOfThreeInternal(ReadOnlySpan<char> text, int at)
    {
        if (at + 3 > text.Length)
            return false;

        for (var i = at; i < at + 3; i++)
        {
            if (!char.IsDigit(text[i]))
                return false;
        }

        return at + 3 == text.Length || !char.IsDigit(text[at + 3]);
    }

    private static bool IsWordCharInternal(char c)
        => c < FIRST_CJK && char.IsLetterOrDigit(c);

    private static int IndexOfPhraseInternal(ReadOnlySpan<char> text, ReadOnlySpan<char> phrase, ReadOnlySpan<char> prefix, int from)
    {
        if (phrase.IsEmpty)
            return -1;

        var offset = from;
        while (offset < text.Length)
        {
            var at = text[offset..].IndexOf(phrase, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
                return -1;

            var start = offset + at;
            var end = start + phrase.Length;
            var matchStart = start - prefix.Length;
            if (matchStart >= 0
                && text.Slice(matchStart, prefix.Length).Equals(prefix, StringComparison.OrdinalIgnoreCase)
                && (matchStart == 0 || !IsWordCharInternal(text[matchStart - 1]))
                && (end == text.Length || !IsWordCharInternal(text[end])))
                return matchStart;

            offset = start + 1;
        }

        return -1;
    }
}
