using System.Buffers;
using System.Text.RegularExpressions;

namespace NadekoBot.Modules.Utility.AiAgent;

/// <summary>
/// Strips potentially dangerous content from user-controlled strings before injecting them into LLM prompts.
/// Preserves legitimate Discord mention formats.
/// </summary>
public static partial class PromptSanitizer
{
    private static readonly SearchValues<char> _xmlSpecialChars = SearchValues.Create("&<>\"'");

    [GeneratedRegex(@"<(?![@#:][!&]?\d|:\w+:\d)[^>]+>")]
    private static partial Regex DangerousTagRegex();

    [GeneratedRegex(@"[\x00-\x08\x0B\x0C\x0E-\x1F]")]
    private static partial Regex ControlCharRegex();

    /// <summary>
    /// Remove XML/HTML-like tags from input while preserving Discord mentions
    /// (user mentions, channel mentions, role mentions, custom emojis).
    /// Also strips control characters.
    /// </summary>
    public static string Sanitize(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var result = DangerousTagRegex().Replace(input, "");
        result = ControlCharRegex().Replace(result, "");
        return result.Trim();
    }

    /// <summary>
    /// Escapes the 5 XML special characters so the string can be safely embedded
    /// inside XML elements or attributes. Single-pass, one allocation via string.Create.
    /// </summary>
    public static string XmlEscape(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var span = input.AsSpan();
        if (!span.ContainsAny(_xmlSpecialChars))
            return input;

        var extra = 0;
        for (var i = 0; i < span.Length; i++)
        {
            extra += span[i] switch
            {
                '&' => 4,
                '<' => 3,
                '>' => 3,
                '"' => 5,
                '\'' => 5,
                _ => 0
            };
        }

        return string.Create(input.Length + extra, input, static (dest, src) =>
        {
            var pos = 0;
            foreach (var c in src.AsSpan())
            {
                switch (c)
                {
                    case '&':
                        "&amp;".CopyTo(dest[pos..]);
                        pos += 5;
                        break;
                    case '<':
                        "&lt;".CopyTo(dest[pos..]);
                        pos += 4;
                        break;
                    case '>':
                        "&gt;".CopyTo(dest[pos..]);
                        pos += 4;
                        break;
                    case '"':
                        "&quot;".CopyTo(dest[pos..]);
                        pos += 6;
                        break;
                    case '\'':
                        "&apos;".CopyTo(dest[pos..]);
                        pos += 6;
                        break;
                    default:
                        dest[pos++] = c;
                        break;
                }
            }
        });
    }
}
