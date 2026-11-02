using System.IO;
using NadekoBot.Services;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;

namespace NadekoBot.Tests;

public class ImageFormatPolicyTests
{
    [Test]
    public void Apply_RefusesTiffAndKeepsPng()
    {
        using var img = new Image<Rgba32>(4, 4, new Rgba32(10, 20, 30, 255));
        using var tiff = new MemoryStream();
        img.SaveAsTiff(tiff);
        using var png = new MemoryStream();
        img.SaveAsPng(png);

        var tiffBytes = tiff.ToArray();
        Assert.Throws<UnknownImageFormatException>(() => Image.Load<Rgba32>(tiffBytes));
        Assert.Throws<UnknownImageFormatException>(() => Image.Load<Rgba32>(new DecoderOptions { MaxFrames = 1 }, tiffBytes));
        Assert.Throws<UnknownImageFormatException>(() => Image.Identify(tiffBytes));
        Assert.That(ImageCache.IsWithinPixelLimit(tiffBytes), Is.False);

        using var loaded = Image.Load<Rgba32>(png.ToArray());
        Assert.That(loaded[0, 0], Is.EqualTo(new Rgba32(10, 20, 30, 255)));
    }
}
