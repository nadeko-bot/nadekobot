using SixLabors.Fonts;

namespace NadekoBot.Services;

public sealed class FontProvider : INService
{
    private const string NOTO_SANS_FILE = "NotoSans-Bold.ttf";
    private const string SYMBOLA_FILE = "Symbola-10.24.ttf";
    private const string RESOURCE_PREFIX = "NadekoBot.Fonts.";

    // optional, owners add fonts here to draw special characters
    private const string CUSTOM_FONTS_PATH = "data/fonts";

    private readonly FontCollection _fonts = new();
    private readonly List<FontFamily> _fallBackFonts = [];

    public FontFamily NotoSans { get; }
    public FontFamily Symbola { get; }
    public IReadOnlyList<FontFamily> FallBackFonts => _fallBackFonts;

    public FontProvider()
    {
        NotoSans = AddBuiltInInternal(NOTO_SANS_FILE);
        Symbola = AddBuiltInInternal(SYMBOLA_FILE);

        if (OperatingSystem.IsWindows())
        {
            var systemFonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            TryAddFallbackInternal(Path.Combine(systemFonts, "seguiemj.ttf"));
            TryAddFallbackInternal(Path.Combine(systemFonts, "msgothic.ttc"));
            TryAddFallbackInternal(Path.Combine(systemFonts, "segoe.ttc"));
        }

        _fallBackFonts.Add(NotoSans);
        _fallBackFonts.Add(Symbola);

        AddCustomFontsInternal();
    }

    private FontFamily AddBuiltInInternal(string fileName)
    {
        using var stream = typeof(FontProvider).Assembly.GetManifestResourceStream(RESOURCE_PREFIX + fileName)
                           ?? throw new InvalidOperationException($"The font {fileName} is not embedded in the bot.");

        return _fonts.Add(stream);
    }

    private void AddCustomFontsInternal()
    {
        if (!Directory.Exists(CUSTOM_FONTS_PATH))
            return;

        try
        {
            foreach (var font in Directory.EnumerateFiles(CUSTOM_FONTS_PATH))
            {
                var fileName = Path.GetFileName(font.AsSpan());

                // older versions kept the built-in fonts in data/fonts
                if (fileName.Equals(NOTO_SANS_FILE, StringComparison.OrdinalIgnoreCase)
                    || fileName.Equals(SYMBOLA_FILE, StringComparison.OrdinalIgnoreCase))
                    continue;

                TryAddFallbackInternal(font);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Unable to read the fonts in {FontsPath}", CUSTOM_FONTS_PATH);
        }
    }

    private void TryAddFallbackInternal(string path)
    {
        if (!File.Exists(path))
            return;

        var extension = Path.GetExtension(path.AsSpan());
        try
        {
            if (extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase))
                _fallBackFonts.Add(_fonts.Add(path));
            else if (extension.Equals(".ttc", StringComparison.OrdinalIgnoreCase))
                _fallBackFonts.AddRange(_fonts.AddCollection(path));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Unable to load the font {FontPath}", path);
        }
    }
}
