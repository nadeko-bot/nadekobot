using System;
using System.IO.Compression;
using NadekoBot.Modules.Searches.Charts;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace NadekoBot.Tests.Searches;

public class CandleChartTests
{
    [Test]
    public void Render_DrawsA1200x675PngWithTheChartColors_AndThePngKeepsEveryPixel()
    {
        var candles = new Candle[40];
        var close = 0.0000043;
        var day = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        for (var i = 0; i < candles.Length; i++)
        {
            var open = close;
            close = open * (i % 3 == 0 ? 0.97 : 1.02);
            candles[i] = new(day + (i * 86_400L), open, Math.Max(open, close) * 1.01, Math.Min(open, close) * 0.99, close, 1e9 + (i * 1e7));
        }

        var png = new CandleChart().Render(candles, new("SHIB", "Shiba Inu \u00B7 \u00E9", "CoinMarketCap"));

        using var image = Image.Load<Rgba32>(png);
        Assert.That(image.Width, Is.EqualTo(CandleChart.WIDTH));
        Assert.That(image.Height, Is.EqualTo(CandleChart.HEIGHT));
        Assert.That(Rgb(image[1, 1]), Is.EqualTo(CandleChart.BACKGROUND));

        int up = 0, down = 0;
        image.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                foreach (var px in rows.GetRowSpan(y))
                {
                    var rgb = Rgb(px);
                    up += rgb == CandleChart.UP ? 1 : 0;
                    down += rgb == CandleChart.DOWN ? 1 : 0;
                }
            }
        });

        Assert.That(up, Is.GreaterThan(1000));
        Assert.That(down, Is.GreaterThan(1000));

        // An odd width and rows where each byte differs by 0x80 from its neighbors.
        const int width = 1201, height = 9;
        var pixels = new uint[width * height];
        var rng = new Random(7);
        for (var i = 0; i < pixels.Length; i++)
            pixels[i] = (uint)rng.Next() & 0xFFFFFF;

        for (var x = 0; x < width; x++)
        {
            pixels[(4 * width) + x] = x % 2 == 0 ? 0x000000u : 0x808080u;
            pixels[(5 * width) + x] = x % 2 == 0 ? 0x808080u : 0x000000u;
        }

        foreach (var level in new[] { CompressionLevel.Fastest, CompressionLevel.Optimal })
        {
            using var decoded = Image.Load<Rgba32>(PngWriter.Encode(pixels, width, height, level));
            var wrong = 0;
            decoded.ProcessPixelRows(rows =>
            {
                for (var y = 0; y < rows.Height; y++)
                {
                    var row = rows.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                        wrong += Rgb(row[x]) == pixels[(y * width) + x] ? 0 : 1;
                }
            });

            Assert.That(decoded.Width, Is.EqualTo(width));
            Assert.That(wrong, Is.Zero, level.ToString());
        }
    }

    private static uint Rgb(Rgba32 px)
        => ((uint)px.R << 16) | ((uint)px.G << 8) | px.B;
}
