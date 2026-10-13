using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace NadekoBot.Hosted;

public static class OwnerIdList
{
    public const int MAX_OWNERS = 10;

    private const int MIN_DIGITS = 17;
    private const int MAX_DIGITS = 20;

    // An empty list is refused: a bot without an owner cannot get one back without a shell.
    public static bool TryParse(string?[]? ids, [NotNullWhen(true)] out ulong[]? owners)
    {
        owners = null;
        if (ids is null || ids.Length is 0 or > MAX_OWNERS)
            return false;

        Span<ulong> parsed = stackalloc ulong[MAX_OWNERS];
        var count = 0;
        foreach (var id in ids)
        {
            if (!TryParseIdInternal(id, out var value))
                return false;

            if (parsed[..count].Contains(value))
                continue;

            parsed[count++] = value;
        }

        owners = parsed[..count].ToArray();
        return true;
    }

    private static bool TryParseIdInternal(string? id, out ulong value)
    {
        value = 0;
        return id is { Length: >= MIN_DIGITS and <= MAX_DIGITS }
               && id[0] != '0'
               && !id.AsSpan().ContainsAnyExceptInRange('0', '9')
               && ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
