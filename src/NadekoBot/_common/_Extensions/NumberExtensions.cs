using System.Globalization;

namespace NadekoBot.Extensions;

public static class NumberExtensions
{
    public static DateTimeOffset ToUnixTimestamp(this double number)
        => new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(number);

    public static string ToShortString(this decimal value)
    {
        if (value <= 1_000)
            return Math.Round(value, 2).ToString(CultureInfo.InvariantCulture);
        if (value <= 1_000_000)
            return Math.Round(value, 1).ToString(CultureInfo.InvariantCulture);
        var tokens = "  MBtq";
        var i = 2;
        while (true)
        {
            var num = (decimal)Math.Pow(1000, i);
            if (num > value)
            {
                var num2 = (decimal)Math.Pow(1000, i - 1);
                return $"{Math.Round((value / num2), 1)}{tokens[i - 1]}".Trim();
            }

            i++;
        }
    }
}