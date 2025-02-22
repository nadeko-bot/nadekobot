using NadekoBot.Common.Yml;
using System.Text;
using System.Text.RegularExpressions;

namespace NadekoBot.Extensions;

public static class StringExtensions
{
    private static readonly HashSet<char> _lettersAndDigits =
    [
        ..Enumerable.Range(48, 10)
                    .Concat(Enumerable.Range(65, 26))
                    .Concat(Enumerable.Range(97, 26))
                    .Select(x => (char)x)
    ];

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

    //http://www.dotnetperls.com/levenshtein
    public static int LevenshteinDistance(this string s, string t)
    {
        var n = s.Length;
        var m = t.Length;
        var d = new int[n + 1, m + 1];

        // Step 1
        if (n == 0)
            return m;

        if (m == 0)
            return n;

        // Step 2
        for (var i = 0; i <= n; d[i, 0] = i++)
        {
        }

        for (var j = 0; j <= m; d[0, j] = j++)
        {
        }

        // Step 3
        for (var i = 1; i <= n; i++)
            //Step 4
        for (var j = 1; j <= m; j++)
        {
            // Step 5
            var cost = t[j - 1] == s[i - 1] ? 0 : 1;

            // Step 6
            d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
        }

        // Step 7
        return d[n, m];
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
        => txt.All(c => _lettersAndDigits.Contains(c));

    public static string UnescapeUnicodeCodePoints(this string input)
        => _codePointRegex.Replace(input,
            me =>
            {
                var str = me.Groups["code"].Value;
                var newString = str.UnescapeUnicodeCodePoint();
                return newString;
            });
    
}