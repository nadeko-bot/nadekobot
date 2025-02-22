using NadekoBot.Db.Models;
using NadekoBot.Modules.Patronage;
using SixLabors.Fonts;
using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Color = SixLabors.ImageSharp.Color;


namespace NadekoBot.Modules.Games;

public sealed class CaptchaService(FontProvider fonts, IBotCache cache, IPatronageService ps) : INService
{
    private readonly NadekoRandom _rng = new();

    public Image<Rgba32> GetPasswordImage(string password)
    {
        var img = new Image<Rgba32>(60, 34);

        var font = fonts.NotoSans.CreateFont(22);
        var outlinePen = new SolidPen(Color.Black, 0.5f);
        var strikeoutRun = new RichTextRun
        {
            Start = 0,
            End = password.GetGraphemeCount(),
            Font = font,
            StrikeoutPen = new SolidPen(Color.White, 4),
            TextDecorations = TextDecorations.Strikeout
        };

        // draw password on the image
        img.Mutate(x =>
        {
            DrawTextExtensions.DrawText(x,
                new RichTextOptions(font)
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FallbackFontFamilies = fonts.FallBackFonts,
                    Origin = new(30, 15),
                    TextRuns = [strikeoutRun]
                },
                password,
                Brushes.Solid(Color.White),
                outlinePen);
        });

        return img;
    }

    public string GeneratePassword()
    {
        var num = _rng.Next((int)Math.Pow(32, 2) + 1, (int)Math.Pow(32, 3));
        return new kwum(num).ToString();
    }

    private static TypedKey<string> CaptchaPasswordKey(ulong userId)
        => new($"timely_password:{userId}");

    public async Task<string?> GetUserCaptcha(ulong userId)
    {
        var patron = await ps.GetPatronAsync(userId);
        if (patron is Patron p && !p.ValidThru.IsBeforeToday())
            return null;

        var pw = await cache.GetOrAddAsync(CaptchaPasswordKey(userId),
            () =>
            {
                var password = GeneratePassword();
                return Task.FromResult(password)!;
            });

        return pw;
    }

    public ValueTask<bool> ClearUserCaptcha(ulong userId)
        => cache.RemoveAsync(CaptchaPasswordKey(userId));
}