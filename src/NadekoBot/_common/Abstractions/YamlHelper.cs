#nullable disable
namespace NadekoBot.Common.Yml;

public static class YamlHelper
{
    // https://github.com/aaubry/YamlDotNet/blob/0f4cc205e8b2dd8ef6589d96de32bf608a687c6f/YamlDotNet/Core/Scanner.cs#L1687
    /// <summary>
    ///     This is modified code from yamldotnet's repo which handles parsing unicode code points
    ///     it is needed as yamldotnet doesn't support unescaped unicode characters
    /// </summary>
    /// <param name="point">Unicode code point</param>
    /// <returns>Actual character</returns>
    public static string UnescapeUnicodeCodePoint(this string point)
    {
        var character = 0;

        // Scan the character value.

        foreach (var c in point)
        {
            if (!IsHex(c))
                return point;

            character = (character << 4) + AsHex(c);
        }

        // Check the value and write the character.

        if (character is (>= 0xD800 and <= 0xDFFF) or > 0x10FFFF)
            return point;

        return char.ConvertFromUtf32(character);
    }

    public static bool IsHex(char c)
        => c is (>= '0' and <= '9') or (>= 'A' and <= 'F') or (>= 'a' and <= 'f');

    public static int AsHex(char c)
    {
        if (c <= '9')
            return c - '0';

        if (c <= 'F')
            return c - 'A' + 10;

        return c - 'a' + 10;
    }
}