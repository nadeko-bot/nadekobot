using System.Globalization;

namespace NadekoBot.Modules.Gambling;

public readonly record struct DndRoll(int Count, int Sides, long Add, long Sub)
{
    public bool IsFate
        => Sides == 0;
}

public static class DiceParser
{
    public const int MAX_DICE = 50;
    public const int MAX_SIDES = 100_000;
    public const int MAX_FATE_DICE = 100;

    // a single number N rolls between 0 and N, "A-B" rolls between A and B; both may be negative
    public static bool TryParseRange(ReadOnlySpan<char> input, out long min, out long max)
    {
        min = 0;
        max = 0;
        input = input.Trim();
        if (input.IsEmpty)
            return false;

        var sep = input[1..].IndexOf('-');
        if (sep < 0)
        {
            if (!TryParseInt(input, out var n))
                return false;

            (min, max) = n < 0 ? (n, 0) : (0, n);
            return true;
        }

        sep++;
        if (!TryParseInt(input[..sep], out var a) || !TryParseInt(input[(sep + 1)..], out var b))
            return false;

        (min, max) = (a, b);
        return true;
    }

    // [count]d(sides|F)[+add][-sub]..., count defaults to 1
    public static bool TryParseDnd(ReadOnlySpan<char> input, out DndRoll roll)
    {
        roll = default;
        input = input.Trim();

        var d = input.IndexOfAny('d', 'D');
        if (d < 0)
            return false;

        var count = 1;
        if (d > 0 && !TryParseSaturated(input[..d], out count))
            return false;

        var rest = input[(d + 1)..];
        if (rest.Length == 1 && (rest[0] is 'f' or 'F'))
        {
            roll = new(count, 0, 0, 0);
            return true;
        }

        var end = rest.IndexOfAny('+', '-');
        var sidesPart = end < 0 ? rest : rest[..end];
        if (!TryParseSaturated(sidesPart, out var sides) || sides == 0)
            return false;

        long add = 0, sub = 0;
        rest = end < 0 ? [] : rest[end..];
        while (!rest.IsEmpty)
        {
            var sign = rest[0];
            rest = rest[1..];
            var next = rest.IndexOfAny('+', '-');
            var part = next < 0 ? rest : rest[..next];
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                return false;

            if (sign == '+')
                add += value;
            else
                sub += value;

            rest = next < 0 ? [] : rest[next..];
        }

        roll = new(count, sides, add, sub);
        return true;
    }

    // a count or side number too large for int still has the right format, so the caller's limits reject it
    private static bool TryParseSaturated(ReadOnlySpan<char> s, out int value)
    {
        if (int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value))
            return true;

        if (s.IsEmpty || s.ContainsAnyExceptInRange('0', '9'))
            return false;

        value = int.MaxValue;
        return true;
    }

    private static bool TryParseInt(ReadOnlySpan<char> s, out int value)
        => int.TryParse(s.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
}
