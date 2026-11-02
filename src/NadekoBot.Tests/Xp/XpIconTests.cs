using NadekoBot.Extensions;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace NadekoBot.Tests.Xp;

public class XpIconTests
{
    [Test]
    public void ToCircle_CropsToSmoothRoundIcon()
    {
        const int size = 38;
        using var img = new Image<Rgba32>(size * 10, size * 5, new Rgba32(200, 50, 50, 255));

        img.ToCircle(size, size);

        Assert.That(img.Width, Is.EqualTo(size));
        Assert.That(img.Height, Is.EqualTo(size));

        var last = size - 1;
        Assert.That(img[0, 0].A, Is.Zero);
        Assert.That(img[last, 0].A, Is.Zero);
        Assert.That(img[0, last].A, Is.Zero);
        Assert.That(img[last, last].A, Is.Zero);
        var centre = img[size / 2, size / 2];
        Assert.That(centre.A, Is.EqualTo(255));
        Assert.That((int)centre.R, Is.EqualTo(200).Within(2));
        Assert.That((int)centre.G, Is.EqualTo(50).Within(2));

        var partial = 0;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            if (img[x, y].A is > 0 and < 255)
                partial++;
        }

        Assert.That(partial, Is.GreaterThan(0));
    }
}
