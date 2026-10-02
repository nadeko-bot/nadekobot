using System.Buffers;
using System.Globalization;

namespace NadekoBot.Modules.Utility.UnitConversion;

public static class UnitExpressionParser
{
    public const int MAX_TERMS = 8;
    public const int MAX_EXPONENT = 9;

    private static readonly SearchValues<char> _operators = SearchValues.Create("*/·⋅×");
    private static readonly SearchValues<char> _coefficientChars = SearchValues.Create("0123456789.");
    private static readonly SearchValues<char> _superscripts = SearchValues.Create("⁰¹²³⁴⁵⁶⁷⁸⁹⁻");

    // A term is [coefficient]unit[^exp | superscript exp | trailing digits], e.g. "100km", "m^2", "m²", "ft3".
    // Terms join with * or /, left to right, so "m/s/s" is m*s^-2.
    public static ResolveStatus Parse(
        ReadOnlySpan<char> expr,
        CurrencyRates rates,
        out ResolvedUnit unit,
        out Range failed)
    {
        failed = default;
        var status = UnitCatalog.Resolve(expr, rates, out unit);
        if (status == ResolveStatus.Ok)
            return status;

        if (expr.IsEmpty)
            return ResolveStatus.Invalid;

        var factor = 1d;
        var dim = default(Dimension);
        var sign = 1;
        var pos = 0;
        var isRatio = false;

        for (var terms = 1;; terms++)
        {
            if (terms > MAX_TERMS)
            {
                failed = Range.All;
                return ResolveStatus.Invalid;
            }

            var rel = expr[pos..].IndexOfAny(_operators);
            var end = rel < 0 ? expr.Length : pos + rel;
            var termStatus = ParseTerm(expr[pos..end], rates, out var coef, out var termUnit, out var exp);
            if (termStatus != ResolveStatus.Ok)
            {
                failed = pos..end;
                return termStatus;
            }

            var power = exp * sign;
            factor *= Math.Pow(coef, sign) * Math.Pow(termUnit.Factor, power);
            if (!dim.TryCombine(termUnit.Dim, power, out dim))
            {
                failed = Range.All;
                return ResolveStatus.Invalid;
            }

            if (end == expr.Length)
                break;

            sign = expr[end] == '/' ? -1 : 1;
            isRatio |= sign < 0;
            pos = end + 1;
        }

        if (!double.IsFinite(factor) || factor <= 0)
        {
            failed = Range.All;
            return ResolveStatus.Invalid;
        }

        // Compound units drop temperature offsets: inside J/K a kelvin is a temperature difference.
        unit = new(factor, 0, dim, UnitKind.Compound, isRatio: isRatio);
        return ResolveStatus.Ok;
    }

    private static ResolveStatus ParseTerm(
        ReadOnlySpan<char> term,
        CurrencyRates rates,
        out double coef,
        out ResolvedUnit unit,
        out int exp)
    {
        coef = 1;
        exp = 1;
        unit = default;
        if (term.IsEmpty)
            return ResolveStatus.Invalid;

        var coefLen = term.IndexOfAnyExcept(_coefficientChars);
        if (coefLen < 0)
            return ResolveStatus.Invalid;

        if (coefLen > 0)
        {
            if (!double.TryParse(term[..coefLen], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
                    out coef)
                || !double.IsFinite(coef)
                || coef <= 0)
                return ResolveStatus.Invalid;

            term = term[coefLen..];
        }

        var whole = UnitCatalog.Resolve(term, rates, out unit);
        if (whole == ResolveStatus.Ok)
            return whole;

        if (!TrySplitExponent(term, out var name, out exp))
            return ResolveStatus.Invalid;

        if (name.Length == term.Length)
            return whole;

        return UnitCatalog.Resolve(name, rates, out unit);
    }

    private static bool TrySplitExponent(ReadOnlySpan<char> term, out ReadOnlySpan<char> name, out int exp)
    {
        name = term;
        exp = 1;

        var caret = term.IndexOf('^');
        if (caret >= 0)
        {
            name = term[..caret];
            return TryParseExponent(term[(caret + 1)..], out exp) && !name.IsEmpty;
        }

        var superStart = term.LastIndexOfAnyExcept(_superscripts) + 1;
        if (superStart < term.Length)
        {
            name = term[..superStart];
            Span<char> digits = stackalloc char[term.Length - superStart];
            for (var i = 0; i < digits.Length; i++)
                digits[i] = FromSuperscript(term[superStart + i]);

            return TryParseExponent(digits, out exp) && !name.IsEmpty;
        }

        var digitStart = term.LastIndexOfAnyExceptInRange('0', '9') + 1;
        if (digitStart > 0 && digitStart < term.Length)
        {
            name = term[..digitStart];
            return TryParseExponent(term[digitStart..], out exp);
        }

        return true;
    }

    private static bool TryParseExponent(ReadOnlySpan<char> s, out int exp)
        => int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exp)
           && exp != 0
           && Math.Abs(exp) <= MAX_EXPONENT;

    private static char FromSuperscript(char c)
        => c switch
        {
            '⁰' => '0',
            '¹' => '1',
            '²' => '2',
            '³' => '3',
            '⁴' => '4',
            '⁵' => '5',
            '⁶' => '6',
            '⁷' => '7',
            '⁸' => '8',
            '⁹' => '9',
            _ => '-'
        };
}
