using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Color = Discord.Color;

namespace NadekoBot.Extensions;

public static class ImagesharpExtensions
{
    private static readonly GraphicsOptions _keepInsideMask = new()
    {
        AlphaCompositionMode = PixelAlphaCompositionMode.DestIn
    };

    // Crops the image to the size without stretching and keeps only an anti-aliased ellipse.
    public static void ToCircle(this Image<Rgba32> img, int width, int height)
    {
        img.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new(width, height),
            Mode = ResizeMode.Crop,
            Sampler = KnownResamplers.Lanczos3
        }));

        using var mask = new Image<Rgba32>(width, height);
        mask.Mutate(m => m.Fill(SixLabors.ImageSharp.Color.White,
            new EllipsePolygon(width / 2f, height / 2f, width, height)));
        img.Mutate(x => x.DrawImage(mask, _keepInsideMask));
    }

    public static Color ToDiscordColor(this Rgba32 color)
        => new(color.R, color.G, color.B);

    public static MemoryStream ToStream(this Image<Rgba32> img, IImageFormat? format = null)
    {
        var imageStream = new MemoryStream();
        if (format?.Name == "GIF")
            img.SaveAsGif(imageStream);
        else
        {
            img.SaveAsPng(imageStream,
                new()
                {
                    ColorType = PngColorType.RgbWithAlpha,
                    CompressionLevel = PngCompressionLevel.DefaultCompression
                });
        }

        imageStream.Position = 0;
        return imageStream;
    }

    public static async Task<MemoryStream> ToStreamAsync(this Image<Rgba32> img, IImageFormat? format = null)
    {
        var imageStream = new MemoryStream();
        if (format?.Name == "GIF")
        {
            await img.SaveAsGifAsync(imageStream);
        }
        else
        {
            await img.SaveAsPngAsync(imageStream,
                new PngEncoder()
                {
                    ColorType = PngColorType.RgbWithAlpha,
                    CompressionLevel = PngCompressionLevel.DefaultCompression
                });
        }

        imageStream.Position = 0;
        return imageStream;
    }
}