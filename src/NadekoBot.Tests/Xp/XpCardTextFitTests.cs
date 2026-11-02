using NadekoBot.Modules.Xp;
using NadekoBot.Modules.Xp.Services;
using NadekoBot.Services;
using NUnit.Framework;
using SixLabors.Fonts;
using SixLabors.ImageSharp;

namespace NadekoBot.Tests.Xp;

public class XpCardTextFitTests
{
    [Test]
    public void LongRankAndLevelFitAndKeepTheBaselineOfOneDigit()
    {
        var fonts = new FontProvider();
        var user = new XpTemplate().User;

        AssertFitsOnBaseline(fonts.NotoSans, user.Rank.FontSize, user.Rank.Pos, XpService.RANK_MAX_WIDTH, "12345");
        AssertFitsOnBaseline(fonts.NotoSans, user.Level.FontSize, user.Level.Pos, XpService.LEVEL_MAX_WIDTH, "1500");
    }

    private static void AssertFitsOnBaseline(FontFamily family, int fontSize, XpTemplatePos templatePos, int maxWidth, string longText)
    {
        var pos = new PointF(templatePos.X, templatePos.Y);
        var (oneFont, oneOrigin) = XpService.GetFitLayout(family, fontSize, "1", maxWidth, pos);
        var (longFont, longOrigin) = XpService.GetFitLayout(family, fontSize, longText, maxWidth, pos);

        var one = TextMeasurer.MeasureBounds("1", new TextOptions(oneFont) { Origin = oneOrigin });
        var lng = TextMeasurer.MeasureBounds(longText, new TextOptions(longFont) { Origin = longOrigin });

        Assert.Multiple(() =>
        {
            Assert.That(oneFont.Size, Is.EqualTo(fontSize), longText);
            Assert.That(oneOrigin, Is.EqualTo(pos), longText);
            Assert.That(longFont.Size, Is.LessThan(fontSize).And.GreaterThan(fontSize * 0.6f), longText);
            Assert.That(lng.Width, Is.LessThanOrEqualTo(maxWidth + 0.5f), longText);
            Assert.That(lng.Bottom, Is.EqualTo(one.Bottom).Within(0.5f), longText);
            Assert.That(lng.Left, Is.EqualTo(one.Left).Within(2f), longText);
        });
    }
}
