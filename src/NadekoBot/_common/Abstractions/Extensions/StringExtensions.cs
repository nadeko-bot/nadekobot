using NadekoBot.Common.Yml;
using System.Text;
using System.Text.RegularExpressions;

namespace NadekoBot.Extensions;

public static class StringExtensions
{
    private static readonly Regex _filterRegex = new(@"discord(?:\.gg|\.io|\.me|\.li|(?:app)?\.com\/invite)\/(\w+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _codePointRegex =
        new(@"(\\U(?<code>[a-zA-Z0-9]{8})|\\u(?<code>[a-zA-Z0-9]{4})|\\x(?<code>[a-zA-Z0-9]{2}))",
            RegexOptions.Compiled);

    public static string PadBoth(this string str, int length)
    {
        var spaces = length - str.Length;
        var padLeft = (spaces / 2) + str.Length;
        return str.PadLeft(padLeft, ' ').PadRight(length, ' ');
    }

    public static string StripHtml(this string input)
        => Regex.Replace(input, "<.*?>", string.Empty);

    public static string? TrimTo(this string? str, int maxLength, bool hideDots = false)
    {
        if (hideDots)
        {
            return str?.Substring(0, Math.Min(str?.Length ?? 0, maxLength));
        }

        if (str is null || str.Length <= maxLength)
            return str;

        return string.Concat(str.AsSpan(0, maxLength - 1), "…");
    }

    public static string ToTitleCase(this string str)
    {
        var tokens = str.Split([" "], StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            tokens[i] = token[..1].ToUpperInvariant() + token[1..];
        }

        return tokens.Join(" ").Replace(" Of ", " of ").Replace(" The ", " the ");
    }

    public static int LevenshteinDistance(this string s, string t)
        => LevenshteinDistanceInternal(s, t, ignoreCase: false);

    public static int LevenshteinDistance(this string s, string t, bool ignoreCase)
        => LevenshteinDistanceInternal(s, t, ignoreCase);

    private static int LevenshteinDistanceInternal(string s, string t, bool ignoreCase)
    {
        var n = s.Length;
        var m = t.Length;

        if (n == 0) return m;
        if (m == 0) return n;

        if (n < m)
            (s, t, n, m) = (t, s, m, n);

        Span<int> prev = stackalloc int[m + 1];
        Span<int> curr = stackalloc int[m + 1];

        for (var j = 0; j <= m; j++)
            prev[j] = j;

        for (var i = 1; i <= n; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= m; j++)
            {
                var sc = s[i - 1];
                var tc = t[j - 1];
                if (ignoreCase)
                {
                    sc = char.ToUpperInvariant(sc);
                    tc = char.ToUpperInvariant(tc);
                }

                var cost = sc == tc ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(prev[j] + 1, curr[j - 1] + 1),
                    prev[j - 1] + cost);
            }

            var tmp = prev;
            prev = curr;
            curr = tmp;
        }

        return prev[m];
    }

    public static async Task<Stream> ToStream(this string str)
    {
        var ms = new MemoryStream();
        var sw = new StreamWriter(ms);
        await sw.WriteAsync(str);
        await sw.FlushAsync();
        ms.Position = 0;
        return ms;
    }

    public static bool IsDiscordInvite(this string str)
        => _filterRegex.IsMatch(str);

    public static string Unmention(this string str)
        => str.Replace("@", "ම", StringComparison.InvariantCulture);

    public static string SanitizeMentions(this string str, bool sanitizeRoleMentions = false)
    {
        str = str.Replace("@everyone", "@everyοne", StringComparison.InvariantCultureIgnoreCase)
                 .Replace("@here", "@һere", StringComparison.InvariantCultureIgnoreCase);
        if (sanitizeRoleMentions)
            str = str.SanitizeRoleMentions();

        return str;
    }

    public static string SanitizeRoleMentions(this string str)
        => str.Replace("<@&", "<ම&", StringComparison.InvariantCultureIgnoreCase);

    public static string SanitizeAllMentions(this string str)
        => str.SanitizeMentions().SanitizeRoleMentions();

    public static string ToBase64(this string plainText)
    {
        var plainTextBytes = Encoding.UTF8.GetBytes(plainText);
        return Convert.ToBase64String(plainTextBytes);
    }

    public static string GetInitials(this string txt, string glue = "")
        => txt.Split(' ').Select(x => x.FirstOrDefault()).Join(glue);

    public static bool IsAlphaNumeric(this string txt)
        => txt.All(char.IsAsciiLetterOrDigit);

    public static string UnescapeUnicodeCodePoints(this string input)
        => _codePointRegex.Replace(input,
            me =>
            {
                var str = me.Groups["code"].Value;
                var newString = str.UnescapeUnicodeCodePoint();
                return newString;
            });
    
}