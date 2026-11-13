namespace NadekoBot.Modules.Searches.Charts;

// A canvas over row-major 0x00RRGGBB pixels. The drawing methods clip to the canvas.
public readonly ref struct ChartCanvas
{
    private readonly Span<uint> _pixels;
    private readonly int _width;
    private readonly int _height;

    public ChartCanvas(Span<uint> pixels, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pixels.Length, width * height);

        _pixels = pixels;
        _width = width;
        _height = height;
    }

    public void Clear(uint color)
        => _pixels[..(_width * _height)].Fill(color);

    public void FillRect(int x0, int y0, int x1, int y1, uint color)
    {
        if (!ClipInternal(ref x0, ref y0, ref x1, ref y1))
            return;

        for (var y = y0; y < y1; y++)
            _pixels.Slice((y * _width) + x0, x1 - x0).Fill(color);
    }

    public void BlendRect(int x0, int y0, int x1, int y1, uint color, int alpha)
    {
        if (!ClipInternal(ref x0, ref y0, ref x1, ref y1))
            return;

        for (var y = y0; y < y1; y++)
        {
            var row = _pixels.Slice((y * _width) + x0, x1 - x0);
            for (var i = 0; i < row.Length; i++)
                row[i] = Blend(row[i], color, alpha);
        }
    }

    public void DashedHLine(int x0, int x1, int y, int thickness, int dash, int gap, uint color)
    {
        for (var x = x0; x < x1; x += dash + gap)
            FillRect(x, y, Math.Min(x + dash, x1), y + thickness, color);
    }

    public void FillRoundedRect(int x0, int y0, int x1, int y1, int radius, uint color)
    {
        radius = Math.Min(radius, Math.Min(x1 - x0, y1 - y0) / 2);
        FillRect(x0, y0 + radius, x1, y1 - radius, color);
        FillRect(x0 + radius, y0, x1 - radius, y0 + radius, color);
        FillRect(x0 + radius, y1 - radius, x1 - radius, y1, color);

        CornerInternal(x0, y0, x0 + radius, y0 + radius, radius, color);
        CornerInternal(x1 - radius, y0, x1 - radius, y0 + radius, radius, color);
        CornerInternal(x0, y1 - radius, x0 + radius, y1 - radius, radius, color);
        CornerInternal(x1 - radius, y1 - radius, x1 - radius, y1 - radius, radius, color);
    }

    public float DrawText(ChartFont font, ReadOnlySpan<char> text, float x, int baseline, uint color, int alpha = 255)
    {
        var phases = font.Phases;
        foreach (var c in text)
        {
            var pen = MathF.Floor(x);
            var phase = (int)MathF.Round((x - pen) * phases);
            var penX = (int)pen;
            if (phase == phases)
            {
                penX++;
                phase = 0;
            }

            var glyph = font.Get(c, phase);
            BlitInternal(glyph, penX + glyph.Left, baseline + glyph.Top, color, alpha);
            x += font.Advance(c);
        }

        return x;
    }

    public static uint Blend(uint dst, uint src, int alpha)
    {
        var a = (uint)alpha;
        var inv = 255 - a;
        var r = ((((src >> 16) & 0xFF) * a) + (((dst >> 16) & 0xFF) * inv) + 127) / 255;
        var g = ((((src >> 8) & 0xFF) * a) + (((dst >> 8) & 0xFF) * inv) + 127) / 255;
        var b = (((src & 0xFF) * a) + ((dst & 0xFF) * inv) + 127) / 255;
        return (r << 16) | (g << 8) | b;
    }

    private void CornerInternal(int boxX, int boxY, float centerX, float centerY, int radius, uint color)
    {
        for (var y = Math.Max(boxY, 0); y < Math.Min(boxY + radius, _height); y++)
        {
            for (var x = Math.Max(boxX, 0); x < Math.Min(boxX + radius, _width); x++)
            {
                var dx = x + 0.5f - centerX;
                var dy = y + 0.5f - centerY;
                var coverage = radius - MathF.Sqrt((dx * dx) + (dy * dy)) + 0.5f;
                if (coverage <= 0)
                    continue;

                ref var px = ref _pixels[(y * _width) + x];
                px = coverage >= 1 ? color : Blend(px, color, (int)(coverage * 255));
            }
        }
    }

    private void BlitInternal(ChartGlyph glyph, int left, int top, uint color, int alpha)
    {
        var x0 = Math.Max(0, -left);
        var x1 = Math.Min(glyph.Width, _width - left);
        var y0 = Math.Max(0, -top);
        var y1 = Math.Min(glyph.Height, _height - top);
        if (x0 >= x1 || y0 >= y1)
            return;

        for (var gy = y0; gy < y1; gy++)
        {
            var src = glyph.Coverage.AsSpan((gy * glyph.Width) + x0, x1 - x0);
            var dst = _pixels.Slice(((top + gy) * _width) + left + x0, x1 - x0);
            for (var i = 0; i < src.Length; i++)
            {
                int a = src[i];
                if (a == 0)
                    continue;

                if (alpha != 255)
                    a = ((a * alpha) + 127) / 255;

                dst[i] = a == 255 ? color : Blend(dst[i], color, a);
            }
        }
    }

    private bool ClipInternal(ref int x0, ref int y0, ref int x1, ref int y1)
    {
        x0 = Math.Max(x0, 0);
        y0 = Math.Max(y0, 0);
        x1 = Math.Min(x1, _width);
        y1 = Math.Min(y1, _height);
        return x0 < x1 && y0 < y1;
    }
}
