#nullable disable
using SixLabors.Fonts;

namespace NadekoBot.Services;

public class FontProvider : INService
{
    public FontFamily DottyFont { get; }

    public FontFamily UniSans { get; }

    public FontFamily NotoSans { get; }
    //public FontFamily Emojis { get; }

    public List<FontFamily> FallBackFonts { get; }
    private readonly FontCollection _fonts;

    public FontProvider()
    {
        _fonts = new();

        NotoSans = _fonts.Add("data/fonts/NotoSans-Bold.ttf");
        UniSans = _fonts.Add("data/fonts/Uni Sans.ttf");

        FallBackFonts = new();

        //FallBackFonts.Add(_fonts.Install("data/fonts/OpenSansEmoji.ttf"));

        // try loading some emoji and jap fonts on windows as fallback fonts
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            try
            {
                var fontsfolder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                FallBackFonts.Add(_fonts.Add(Path.Combine(fontsfolder, "seguiemj.ttf")));
                FallBackFonts.AddRange(_fonts.AddCollection(Path.Combine(fontsfolder, "msgothic.ttc")));
                FallBackFonts.AddRange(_fonts.AddCollection(Path.Combine(fontsfolder, "segoe.ttc")));
            }
            catch { }
        }

        // any fonts present in data/fonts should be added as fallback fonts
        // this will allow support for special characters when drawing text
        foreach (var font in Directory.GetFiles(@"data/fonts"))
        {
            if (font.EndsWith(".ttf"))
                FallBackFonts.Add(_fonts.Add(font));
            else if (font.EndsWith(".ttc"))
                FallBackFonts.AddRange(_fonts.AddCollection(font));
        }

        DottyFont = FallBackFonts.First(x => x.Name == "dotty");
    }
}