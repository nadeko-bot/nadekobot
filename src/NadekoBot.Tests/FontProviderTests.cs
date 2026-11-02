using NadekoBot.Services;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace NadekoBot.Tests;

public class FontProviderTests
{
    [Test]
    public void BuiltInFontsLoadFromTheBotAssemblyAndDraw()
    {
        var fonts = new FontProvider();

        Assert.That(fonts.NotoSans.Name, Is.EqualTo("Noto Sans"));
        Assert.That(fonts.Symbola.Name, Is.EqualTo("Symbola"));
        Assert.That(fonts.FallBackFonts, Does.Contain(fonts.NotoSans).And.Contain(fonts.Symbola));

        using var img = new Image<Rgba32>(200, 40);
        img.Mutate(x => x.DrawText(
            new RichTextOptions(fonts.NotoSans.CreateFont(20)) { FallbackFontFamilies = fonts.FallBackFonts },
            "Nadeko \u2665\u265B",
            Color.White));

        var drawn = false;
        img.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height && !drawn; y++)
            {
                foreach (var px in rows.GetRowSpan(y))
                {
                    if (px.A == 0)
                        continue;

                    drawn = true;
                    break;
                }
            }
        });

        Assert.That(drawn, Is.True);
    }
}
