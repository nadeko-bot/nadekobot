using System.Globalization;

namespace NadekoBot.Modules.Utility.UnitConversion;

public readonly ref struct ConvertInput
{
    public readonly double Value;
    public readonly ReadOnlySpan<char> From;
    public readonly ReadOnlySpan<char> To;

    public ConvertInput(double value, ReadOnlySpan<char> from, ReadOnlySpan<char> to)
    {
        Value = value;
        From = from;
        To = to;
    }

    public bool HasTarget => !To.IsEmpty;
}

public static class ConvertInputParser
{
    private const int MAX_TOKENS = 4;
    private const int MAX_NUMBER_LENGTH = 64;
    private const int GROUP_SIZE = 3;
    private const string SEPARATORS = " \t\r\n";

    // Accepts "5 km mi", "5km mi", "5 km to mi", "5 km" and the old order "km mi 5".
    public static bool TryParse(ReadOnlySpan<char> input, CultureInfo culture, out ConvertInput result)
    {
        result = default;
        Span<Range> tokens = stackalloc Range[MAX_TOKENS + 1];
        var count = input.SplitAny(tokens, SEPARATORS,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (count is 0 or > MAX_TOKENS)
            return false;

        var first = input[tokens[0]];
        double value;
        ReadOnlySpan<char> from;
        int next;

        if (TryParseNumber(first, culture, out value))
        {
            if (count < 2)
                return false;

            from = input[tokens[1]];
            next = 2;
        }
        else if (TrySplitLeadingNumber(first, culture, out value, out from))
        {
            next = 1;
        }
        else if (count == 3 && TryParseNumber(input[tokens[2]], culture, out value))
        {
            result = new(value, first, input[tokens[1]]);
            return true;
        }
        else
        {
            return false;
        }

        var remaining = count - next;
        if (remaining == 0)
        {
            result = new(value, from, default);
            return true;
        }

        if (remaining == 1)
        {
            result = new(value, from, input[tokens[next]]);
            return true;
        }

        if (remaining == 2 && IsConnector(input[tokens[next]]))
        {
            result = new(value, from, input[tokens[next + 1]]);
            return true;
        }

        return false;
    }

    private static bool IsConnector(ReadOnlySpan<char> s)
        => s.Equals("to", StringComparison.InvariantCultureIgnoreCase)
           || s.Equals("in", StringComparison.InvariantCultureIgnoreCase)
           || s.Equals("into", StringComparison.InvariantCultureIgnoreCase)
           || s.Equals("as", StringComparison.InvariantCultureIgnoreCase)
           || s is "->" or "=" or "→";

    // Splits "5km" or "-2.5e3m" into the number and the unit. "5eV" keeps "eV" as the unit.
    private static bool TrySplitLeadingNumber(
        ReadOnlySpan<char> token,
        CultureInfo culture,
        out double value,
        out ReadOnlySpan<char> unit)
    {
        value = 0;
        unit = default;

        var i = 0;
        if (i < token.Length && token[i] is '-' or '+')
            i++;

        var digits = 0;
        while (i < token.Length)
        {
            var c = token[i];
            if (char.IsAsciiDigit(c))
            {
                digits++;
                i++;
            }
            else if (c is '.' or ',' or '_' or '\'')
            {
                i++;
            }
            else if (c is 'e' or 'E' && digits > 0 && IsExponentStart(token, i + 1))
            {
                i += token[i + 1] is '-' or '+' ? 2 : 1;
            }
            else
            {
                break;
            }
        }

        if (digits == 0 || i >= token.Length)
            return false;

        unit = token[i..];
        return TryParseNumber(token[..i], culture, out value);
    }

    private static bool IsExponentStart(ReadOnlySpan<char> token, int at)
    {
        if (at < token.Length && token[at] is '-' or '+')
            at++;

        return at < token.Length && char.IsAsciiDigit(token[at]);
    }

    // Tries the server culture first, then the invariant culture. A group separator only counts when the
    // groups have exactly three digits, so "1.5" on a German server is 1.5 and not 15.
    public static bool TryParseNumber(ReadOnlySpan<char> s, CultureInfo culture, out double value)
    {
        value = 0;
        if (s.IsEmpty || s.Length > MAX_NUMBER_LENGTH)
            return false;

        Span<char> buf = stackalloc char[MAX_NUMBER_LENGTH];
        var n = 0;
        foreach (var c in s)
        {
            if (c is not ('_' or '\''))
                buf[n++] = c;
        }

        var span = buf[..n];
        return TryParseWith(span, culture.NumberFormat, out value)
               || TryParseWith(span, NumberFormatInfo.InvariantInfo, out value);
    }

    private static bool TryParseWith(ReadOnlySpan<char> s, NumberFormatInfo nfi, out double value)
    {
        value = 0;
        return HasValidGrouping(s, nfi)
               && double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, nfi, out value)
               && double.IsFinite(value);
    }

    private static bool HasValidGrouping(ReadOnlySpan<char> s, NumberFormatInfo nfi)
    {
        var group = nfi.NumberGroupSeparator;
        if (string.IsNullOrEmpty(group))
            return true;

        var dec = s.IndexOf(nfi.NumberDecimalSeparator, StringComparison.Ordinal);
        if (dec >= 0 && s[(dec + 1)..].Contains(group, StringComparison.Ordinal))
            return false;

        var integer = dec >= 0 ? s[..dec] : s;
        if (integer.Length > 0 && integer[0] is '-' or '+')
            integer = integer[1..];

        var firstGroup = true;
        while (true)
        {
            var idx = integer.IndexOf(group, StringComparison.Ordinal);
            var part = idx < 0 ? integer : integer[..idx];

            if (firstGroup)
            {
                if (idx < 0)
                    return true;

                if (part.Length is 0 or > GROUP_SIZE)
                    return false;
            }
            else if (part.Length != GROUP_SIZE)
            {
                return false;
            }

            if (idx < 0)
                return true;

            firstGroup = false;
            integer = integer[(idx + group.Length)..];
        }
    }
}

public static class ConvertFormatter
{
    private const int SIGNIFICANT_DIGITS = 12;
    private const double SCIENTIFIC_ABOVE = 1e15;
    private const double SCIENTIFIC_BELOW = 1e-6;
    private const int MAX_DECIMALS = SIGNIFICANT_DIGITS + 5;
    private const string SCIENTIFIC_FORMAT = "0.###########E+0";
    private const string ROUND_FORMAT = "G12";

    private static readonly string[] _fixedFormats = BuildFixedFormats();

    private static string[] BuildFixedFormats()
    {
        var formats = new string[MAX_DECIMALS + 1];
        formats[0] = "#,0";
        for (var i = 1; i < formats.Length; i++)
            formats[i] = "#,0." + new string('#', i);

        return formats;
    }

    public static string FormatValue(double value, CultureInfo culture)
    {
        var rounded = RoundSignificant(value);
        if (rounded == 0)
            return 0.ToString(culture);

        var abs = Math.Abs(rounded);
        if (abs is >= SCIENTIFIC_ABOVE or < SCIENTIFIC_BELOW)
            return rounded.ToString(SCIENTIFIC_FORMAT, culture);

        var magnitude = (int)Math.Floor(Math.Log10(abs));
        var decimals = Math.Clamp(SIGNIFICANT_DIGITS - 1 - magnitude, 0, MAX_DECIMALS);
        return rounded.ToString(_fixedFormats[decimals], culture);
    }

    public static string FormatQuantity(
        double value,
        in ResolvedUnit unit,
        ReadOnlySpan<char> typed,
        CultureInfo culture)
    {
        var number = FormatValue(value, culture);
        if (unit.Kind == UnitKind.Compound)
            return $"{number} {typed.Trim()}";

        var symbol = UnitCatalog.GetSymbol(unit);
        var name = UnitCatalog.GetName(unit, value != 1);
        return name is null || name == symbol
            ? $"{number} {symbol}"
            : $"{number} {symbol} ({name})";
    }

    private static double RoundSignificant(double value)
    {
        Span<char> buf = stackalloc char[32];
        return value.TryFormat(buf, out var written, ROUND_FORMAT, CultureInfo.InvariantCulture)
               && double.TryParse(buf[..written], NumberStyles.Float, CultureInfo.InvariantCulture, out var r)
            ? r
            : value;
    }
}
