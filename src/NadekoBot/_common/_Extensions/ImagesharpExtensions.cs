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
    // https://github.com/SixLabors/Samples/blob/master/ImageSharp/AvatarWithRoundedCorner/Program.cs
    public static IImageProcessingContext ApplyRoundedCorners(this IImageProcessingContext ctx, float cornerRadius)
    {
        var size = ctx.GetCurrentSize();
        var corners = BuildCorners(size.Width, size.Height, cornerRadius);

        ctx.SetGraphicsOptions(new GraphicsOptions
        {
            Antialias = true,
            // enforces that any part of this shape that has color is punched out of the background
            AlphaCompositionMode = PixelAlphaCompositionMode.DestOut
        });

        foreach (var c in corners)
            ctx = ctx.Fill(SixLabors.ImageSharp.Color.Red, c);

        return ctx;
    }

    private static IPathCollection BuildCorners(int imageWidth, int imageHeight, float cornerRadius)
    {
        // first create a square
        var rect = new RectangularPolygon(-0.5f, -0.5f, cornerRadius, cornerRadius);

        // then cut out of the square a circle so we are left with a corner
        var cornerTopLeft = rect.Clip(new EllipsePolygon(cornerRadius - 0.5f, cornerRadius - 0.5f, cornerRadius));

        // corner is now a corner shape positions top left
        //lets make 3 more positioned correctly, we can do that by translating the original around the center of the image

        var rightPos = imageWidth - cornerTopLeft.Bounds.Width + 1;
        var bottomPos = imageHeight - cornerTopLeft.Bounds.Height + 1;

        // move it across the width of the image - the width of the shape
        var cornerTopRight = cornerTopLeft.RotateDegree(90).Translate(rightPos, 0);
        var cornerBottomLeft = cornerTopLeft.RotateDegree(-90).Translate(0, bottomPos);
        var cornerBottomRight = cornerTopLeft.RotateDegree(180).Translate(rightPos, bottomPos);

        return new PathCollection(cornerTopLeft, cornerBottomLeft, cornerTopRight, cornerBottomRight);
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