using System.Text.RegularExpressions;

namespace NadekoBot.Modules.Utility.AiAgent;

/// <summary>
/// Strips potentially dangerous content from user-controlled strings before injecting them into LLM prompts.
/// Preserves legitimate Discord mention formats.
/// </summary>
public static partial class PromptSanitizer
{
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
}
