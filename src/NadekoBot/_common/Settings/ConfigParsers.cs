#nullable disable
using SixLabors.ImageSharp.PixelFormats;
using System.Globalization;

namespace NadekoBot.Services;

/// <summary>
///     Custom setting value parsers for types which don't have them by default
/// </summary>
public static class ConfigParsers
{
    /// <summary>
    ///     Default string parser. Passes input to output and returns true.
    /// </summary>
    public static bool String(string input, out string output)
    {
        output = input;
        return true;
    }

    public static bool Culture(string input, out CultureInfo output)
    {
        try
        {
            output = new(input);
            return true;
        }
        catch
        {
            output = null;
            return false;
        }
    }

    public static bool InsensitiveEnum<T>(string input, out T output)
        where T: struct
        => Enum.TryParse(input, true, out output);
}

public static class ConfigPrinters
{
    public static string ToString<TAny>(TAny input)
        => input.ToString();

    public static string Culture(CultureInfo culture)
        => culture.Name;

    public static string Color(Rgba32 color)
        => ((uint)((color.B << 0) | (color.G << 8) | (color.R << 16))).ToString("X6");
}