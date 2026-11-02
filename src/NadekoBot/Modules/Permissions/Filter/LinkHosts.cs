using System.Buffers;
using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Permissions.Services;

public static class LinkHosts
{
    private const int MAX_LABEL_LENGTH = 63;

    // Browsers end the host at a backslash too, so "https://evil.com\@youtube.com" opens evil.com.
    private static readonly SearchValues<char> _authorityEnd = SearchValues.Create("/?#\\ \t\r\n<>\"'|`^{}()[]*~");

    // True when the text has an http, https or ftp link whose host is not an allowed domain or its subdomain.
    public static bool HasBlockedLink(ReadOnlySpan<char> text, FrozenSet<string>? allowed)
    {
        while (true)
        {
            var i = text.IndexOf("://", StringComparison.Ordinal);
            if (i < 0)
                return false;

            var isLink = IsLinkScheme(text[..i]);
            text = text[(i + 3)..];
            if (!isLink)
                continue;

            // browsers skip extra slashes, so "https:///evil.com" opens evil.com
            text = text.TrimStart("/\\");

            var end = text.IndexOfAny(_authorityEnd);
            var authority = end < 0 ? text : text[..end];
            if (authority.IsEmpty)
            {
                // an ipv6 host, which no domain covers
                if (!text.IsEmpty && text[0] == '[')
                    return true;

                continue;
            }

            var host = GetHost(authority);
            if (host.IsEmpty || allowed is null || allowed.Count == 0 || !IsAllowed(host, allowed))
                return true;
        }
    }

    // The domain in the form the filter stores, or null when the text is not a domain.
    public static string? NormalizeDomain(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var text = input.AsSpan().Trim();
        var scheme = text.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
            text = text[(scheme + 3)..];

        if (text.StartsWith("*."))
            text = text[2..];

        var end = text.IndexOfAny(_authorityEnd);
        if (end >= 0)
            text = text[..end];

        var host = GetHost(text);
        if (host.IsEmpty)
            return null;

        string ascii;
        try
        {
            ascii = new IdnMapping().GetAscii(host.ToString()).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return null;
        }

        return IsValidDomain(ascii) ? ascii : null;
    }

    private static bool IsLinkScheme(ReadOnlySpan<char> before)
        => before.EndsWith("https", StringComparison.OrdinalIgnoreCase)
           || before.EndsWith("http", StringComparison.OrdinalIgnoreCase)
           || before.EndsWith("ftp", StringComparison.OrdinalIgnoreCase);

    // The host of an authority: without user info, port and the dot of a full name.
    private static ReadOnlySpan<char> GetHost(ReadOnlySpan<char> authority)
    {
        var at = authority.LastIndexOf('@');
        if (at >= 0)
            authority = authority[(at + 1)..];

        var colon = authority.IndexOf(':');
        if (colon >= 0)
            authority = authority[..colon];

        return authority.TrimEnd('.');
    }

    private static bool IsAllowed(ReadOnlySpan<char> host, FrozenSet<string> allowed)
    {
        if (!Ascii.IsValid(host))
        {
            // rare, so the allocation of the conversion is fine here
            try
            {
                return IsAllowedAscii(new IdnMapping().GetAscii(host.ToString()), allowed);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        return IsAllowedAscii(host, allowed);
    }

    private static bool IsAllowedAscii(ReadOnlySpan<char> host, FrozenSet<string> allowed)
    {
        var lookup = allowed.GetAlternateLookup<ReadOnlySpan<char>>();
        while (true)
        {
            if (lookup.Contains(host))
                return true;

            var dot = host.IndexOf('.');
            if (dot < 0)
                return false;

            host = host[(dot + 1)..];
        }
    }

    private static bool IsValidDomain(string domain)
    {
        if (domain.Length is 0 or > AllowedLinkDomain.MAX_DOMAIN_LENGTH || !domain.Contains('.'))
            return false;

        foreach (var range in domain.AsSpan().Split('.'))
        {
            var label = domain.AsSpan()[range];
            if (label.Length is 0 or > MAX_LABEL_LENGTH || label[0] == '-' || label[^1] == '-')
                return false;

            foreach (var c in label)
            {
                if (!char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c != '-')
                    return false;
            }
        }

        return true;
    }
}

public enum LinkAllowResult
{
    Added,
    Removed,
    LimitReached
}
