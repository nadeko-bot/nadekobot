using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Color = SixLabors.ImageSharp.Color;

namespace NadekoBot.Modules.Searches.Charts;

public sealed class ChartGlyph(byte[] coverage, int width, int height, int left, int top)
{
    public readonly byte[] Coverage = coverage;
    public readonly int Width = width;
    public readonly int Height = height;

    // Position of the bitmap relative to the pen position and the baseline.
    public readonly int Left = left;
    public readonly int Top = top;
}

// Glyph bitmaps of one font and size. Each glyph is drawn once for each sub-pixel phase with SixLabors.Fonts, and copied after that.
public sealed class ChartFont
{
    private const char FIRST = ' ';
    private const char LAST = '~';
    private const char MIDDLE_DOT = '\u00B7';
    private const int SLOTS = LAST - FIRST + 2;
    private const int DOT_SLOT = SLOTS - 1;
    private const int FALLBACK_SLOT = '?' - FIRST;
    private const int PAD = 2;

    private readonly Font _font;
    private readonly float _baseline;
    private readonly float[] _advances = new float[SLOTS];
    private readonly ChartGlyph?[] _glyphs;

    public int Phases { get; }
    public int CapHeight { get; }

    public ChartFont(FontFamily family, float size, int phases)
    {
        _font = family.CreateFont(size);
        Phases = phases;
        _glyphs = new ChartGlyph?[SLOTS * phases];

        var options = new TextOptions(_font);
        var capital = TextMeasurer.MeasureBounds("H", options);
        _baseline = capital.Bottom;
        CapHeight = (int)MathF.Round(capital.Bottom - capital.Top);

        for (var slot = 0; slot < SLOTS; slot++)
            _advances[slot] = TextMeasurer.MeasureAdvance(CharOf(slot).ToString(), options).Width;
    }

    public float Measure(ReadOnlySpan<char> text)
    {
        var width = 0f;
        foreach (var c in text)
            width += _advances[SlotOf(c)];

        return width;
    }

    public float Advance(char c)
        => _advances[SlotOf(c)];

    public ChartGlyph Get(char c, int phase)
    {
        var slot = SlotOf(c);
        ref var entry = ref _glyphs[(slot * Phases) + phase];
        var glyph = Volatile.Read(ref entry);
        if (glyph is not null)
            return glyph;

        glyph = RenderGlyphInternal(CharOf(slot), phase);
        return Interlocked.CompareExchange(ref entry, glyph, null) ?? glyph;
    }

    private static char CharOf(int slot)
        => slot == DOT_SLOT ? MIDDLE_DOT : (char)(FIRST + slot);

    private static int SlotOf(char c)
        => c is >= FIRST and <= LAST ? c - FIRST : c == MIDDLE_DOT ? DOT_SLOT : FALLBACK_SLOT;

    private ChartGlyph RenderGlyphInternal(char c, int phase)
    {
        var text = c.ToString();
        var bounds = TextMeasurer.MeasureBounds(text, new TextOptions(_font));
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return new([], 0, 0, 0, 0);

        // The baseline falls on a whole pixel row, so the bottoms of digits and capitals stay sharp.
        var penX = PAD - (int)MathF.Floor(bounds.Left);
        var baseRow = PAD + (int)MathF.Ceiling(_baseline - bounds.Top);
        var width = penX + (int)MathF.Ceiling(bounds.Right) + PAD + 1;
        var height = baseRow + (int)MathF.Ceiling(bounds.Bottom - _baseline) + PAD + 1;

        var pixels = new Rgba32[width * height];
        using (var image = new Image<Rgba32>(width, height))
        {
            var options = new RichTextOptions(_font)
            {
                Origin = new PointF(penX + (phase / (float)Phases), baseRow - _baseline)
            };
            image.Mutate(x => x.DrawText(options, text, Color.White));
            image.CopyPixelDataTo(pixels);
        }

        int x0 = width, y0 = height, x1 = -1, y1 = -1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (pixels[(y * width) + x].A == 0)
                    continue;

                x0 = Math.Min(x0, x);
                x1 = Math.Max(x1, x);
                y0 = Math.Min(y0, y);
                y1 = Math.Max(y1, y);
            }
        }

        if (x1 < 0)
            return new([], 0, 0, 0, 0);

        var glyphWidth = x1 - x0 + 1;
        var glyphHeight = y1 - y0 + 1;
        var coverage = new byte[glyphWidth * glyphHeight];
        for (var y = 0; y < glyphHeight; y++)
        {
            for (var x = 0; x < glyphWidth; x++)
                coverage[(y * glyphWidth) + x] = pixels[((y0 + y) * width) + x0 + x].A;
        }

        return new(coverage, glyphWidth, glyphHeight, x0 - penX, y0 - baseRow);
    }
}

// The text sizes of the chart. The fonts are subsets of Noto Sans with printable ASCII and the middle dot, made by scripts/fonts/subset-chart-fonts.sh.
public sealed class ChartFonts
{
    public const string REGULAR_FILE = "NotoSans-Regular.ascii.ttf";
    public const string SEMIBOLD_FILE = "NotoSans-SemiBold.ascii.ttf";
    private const string RESOURCE_PREFIX = "NadekoBot.Fonts.";
    private const int PHASES = 4;

    public ChartFont Symbol { get; }
    public ChartFont Title { get; }
    public ChartFont Legend { get; }
    public ChartFont Axis { get; }
    public ChartFont AxisStrong { get; }
    public ChartFont Watermark { get; }

    public ChartFonts()
    {
        var collection = new FontCollection();
        var regular = LoadInternal(collection, REGULAR_FILE);
        var semiBold = LoadInternal(collection, SEMIBOLD_FILE);

        Symbol = new(semiBold, 30, PHASES);
        Title = new(regular, 24, PHASES);
        Legend = new(regular, 22, PHASES);
        Axis = new(regular, 21, PHASES);
        AxisStrong = new(semiBold, 21, PHASES);
        Watermark = new(semiBold, 150, 1);
    }

    private static FontFamily LoadInternal(FontCollection collection, string fileName)
    {
        using var stream = typeof(ChartFonts).Assembly.GetManifestResourceStream(RESOURCE_PREFIX + fileName)
                           ?? throw new InvalidOperationException($"The font {fileName} is not embedded in the bot.");

        return collection.Add(stream);
    }
}
