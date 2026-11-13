using System.Globalization;

namespace NadekoBot.Modules.Searches.Charts;

// Number formats of the charts and the finance embeds. The span methods return the length that they wrote.
public static class ChartFormat
{
    public const int MAX_DECIMALS = 10;
    private const int PERCENT_DECIMALS = 2;

    // " (+", the percent and "%)".
    private const int CHANGE_SUFFIX_ROOM = 16;

    private static readonly string[] _numberFormats =
        ["N0", "N1", "N2", "N3", "N4", "N5", "N6", "N7", "N8", "N9", "N10"];

    public static string NumberFormat(int decimals)
        => _numberFormats[Math.Clamp(decimals, 0, MAX_DECIMALS)];

    // Two decimals from 1 up, and about 4 significant digits below 1.
    public static int PriceDecimals(double price)
    {
        if (!(price > 0) || price >= 1)
            return 2;

        return Math.Clamp(3 - (int)Math.Floor(Math.Log10(price)), 2, MAX_DECIMALS);
    }

    public static int Price(Span<char> dst, double value, int decimals, IFormatProvider? provider = null)
        => value.TryFormat(dst, out var written, NumberFormat(decimals), provider ?? CultureInfo.InvariantCulture)
            ? written
            : 0;

    // 210.44M, 3.85T
    public static int Short(Span<char> dst, double value, IFormatProvider? provider = null)
    {
        var abs = Math.Abs(value);
        var (divisor, suffix) = abs switch
        {
            >= 1e12 => (1e12, 'T'),
            >= 1e9 => (1e9, 'B'),
            >= 1e6 => (1e6, 'M'),
            >= 1e3 => (1e3, 'K'),
            _ => (1d, '\0')
        };

        if (!(value / divisor).TryFormat(dst, out var written, "0.##", provider ?? CultureInfo.InvariantCulture))
            return 0;

        if (suffix != '\0' && written < dst.Length)
            dst[written++] = suffix;

        return written;
    }

    public static string ShortString(double value, IFormatProvider? provider = null)
    {
        Span<char> buffer = stackalloc char[32];
        return new string(buffer[..Short(buffer, value, provider)]);
    }

    // +6.55 (+3.94%)
    public static int Change(Span<char> dst, double change, double percent, int decimals)
    {
        if (dst.Length < 48)
            return 0;

        var sign = change < 0 ? '-' : '+';
        var n = 0;
        dst[n++] = sign;
        n += Price(dst[n..], Math.Abs(change), decimals);
        if (dst.Length - n < CHANGE_SUFFIX_ROOM)
            return 0;

        dst[n++] = ' ';
        dst[n++] = '(';
        dst[n++] = sign;
        n += Price(dst[n..], Math.Abs(percent), PERCENT_DECIMALS);
        if (dst.Length - n < 2)
            return 0;

        dst[n++] = '%';
        dst[n++] = ')';
        return n;
    }
}
