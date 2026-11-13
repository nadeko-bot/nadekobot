using System.Buffers;
using System.Globalization;
using System.IO.Compression;

namespace NadekoBot.Modules.Searches.Charts;

public readonly record struct ChartHeader(string Symbol, string? Name, string? Venue);

// Draws a daily candle chart in the dark style of TradingView.
public sealed class CandleChart : INService
{
    public const int WIDTH = 1200;
    public const int HEIGHT = 675;
    public const uint UP = 0x089981;
    public const uint DOWN = 0xF23645;
    public const uint BACKGROUND = 0x131722;

    private const uint GRID = 0x1E222D;
    private const uint BORDER = 0x2A2E39;
    private const uint TEXT = 0xD1D4DC;
    private const uint TEXT_SOFT = 0xB2B5BE;
    private const uint TEXT_DIM = 0x787B86;
    private const uint TAG_TEXT = 0xFFFFFF;

    private const int SIDE_PAD = 24;
    private const int TITLE_BASELINE = 46;
    private const int LEGEND_BASELINE = 84;
    private const int PLOT_TOP = 104;
    private const int PLOT_BOTTOM = HEIGHT - 46;
    private const int VOLUME_HEIGHT = (PLOT_BOTTOM - PLOT_TOP) / 5;
    private const int PRICE_TOP = PLOT_TOP + 24;
    private const int PRICE_BOTTOM = PLOT_BOTTOM - VOLUME_HEIGHT - 16;
    private const int TICK_SPACING = 72;
    private const int MAX_TICKS = 16;
    private const int AXIS_PAD = 14;
    private const int MIN_AXIS_WIDTH = 96;
    private const int TAG_PAD = 8;
    private const int TAG_RADIUS = 6;
    private const int RIGHT_SLOTS = 3;
    private const double BODY_RATIO = 0.7;
    private const int VOLUME_ALPHA = 115;
    private const int WATERMARK_ALPHA = 13;
    private const int MAX_TIME_LABELS = 40;
    private const int TIME_LABEL_GAP = 28;
    private const int LEGEND_GAP = 18;
    private const int LEGEND_LABEL_GAP = 6;
    private const int SYMBOL_GAP = 16;
    private const int PRICE_GAP = 14;
    private const int NAME_PRICE_GAP = 32;
    private const int TAG_RIGHT_MARGIN = 6;
    private const int WIDE_WICK_SLOT = 6;
    private const int MIN_BODY_HEIGHT = 2;
    private const int LAST_LINE_HEIGHT = 2;
    private const int DASH_LENGTH = 6;
    private const int SECONDS_PER_DAY = 86_400;
    private const int TEXT_BUFFER = 128;
    private const int LINE_BUFFER = 160;
    private const string INTERVAL = "1D";

    // Half the time of Optimal (about 5 ms instead of 11 ms) for a file of about 73 KB instead of 48 KB.
    private const CompressionLevel PNG_LEVEL = CompressionLevel.Fastest;
    private const string SEPARATOR = " \u00B7 ";
    private const string ELLIPSIS = "...";

    private static readonly int _epochDay = new DateOnly(1970, 1, 1).DayNumber;

    private static readonly string[] _months =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    private readonly Lazy<ChartFonts> _fonts = new(static () => new ChartFonts(), LazyThreadSafetyMode.PublicationOnly);

    public ChartFonts Fonts
        => _fonts.Value;

    public byte[] Render(ReadOnlySpan<Candle> candles, in ChartHeader header)
    {
        if (candles.IsEmpty)
            throw new ArgumentException("A chart needs at least one candle.", nameof(candles));

        var fonts = _fonts.Value;
        var pixels = ArrayPool<uint>.Shared.Rent(WIDTH * HEIGHT);
        try
        {
            var canvas = new ChartCanvas(pixels, WIDTH, HEIGHT);
            DrawInternal(canvas, candles, header, fonts);
            return PngWriter.Encode(pixels.AsSpan(0, WIDTH * HEIGHT), WIDTH, HEIGHT, PNG_LEVEL);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(pixels);
        }
    }

    private readonly struct PriceScale(double min, double scale)
    {
        public int Y(double price)
            => (int)Math.Round(PRICE_BOTTOM - ((price - min) * scale));

        public double PriceAt(int y)
            => min + ((PRICE_BOTTOM - y) / scale);
    }

    private readonly struct CandleLayout(double slot, int body, int wick)
    {
        public readonly int Body = body;
        public readonly int Wick = wick;

        public int Left(int index)
            => (int)Math.Round(((index + 0.5) * slot) - (Body / 2.0));

        public int Center(int index)
            => Left(index) + (Body / 2);
    }

    private static void DrawInternal(ChartCanvas canvas, ReadOnlySpan<Candle> candles, in ChartHeader header, ChartFonts fonts)
    {
        var low = double.MaxValue;
        var high = double.MinValue;
        var maxVolume = 0d;
        foreach (ref readonly var c in candles)
        {
            low = Math.Min(low, c.Low);
            high = Math.Max(high, c.High);
            maxVolume = Math.Max(maxVolume, c.Volume);
        }

        if (high - low <= Math.Abs(high) * 1e-9)
        {
            var pad = high > 0 ? high * 0.01 : 1;
            low -= pad;
            high += pad;
        }

        var scale = new PriceScale(low, (PRICE_BOTTOM - PRICE_TOP) / (high - low));
        ref readonly var last = ref candles[^1];
        var lastColor = last.Close >= last.Open ? UP : DOWN;
        var priceDecimals = ChartFormat.PriceDecimals(last.Close);
        var lastY = scale.Y(last.Close);

        var step = NiceStep((high - low) * TICK_SPACING / (PRICE_BOTTOM - PRICE_TOP));
        var firstTick = Math.Max(0, Math.Ceiling(scale.PriceAt(PLOT_BOTTOM - VOLUME_HEIGHT) / step) * step);
        var topPrice = scale.PriceAt(PLOT_TOP + fonts.Axis.CapHeight);
        var ticks = Math.Clamp((int)Math.Floor(((topPrice - firstTick) / step) + 1e-9) + 1, 0, MAX_TICKS);
        var axisDecimals = high is >= 1 and < 1000
            ? Math.Max(DecimalsOf(step), 2)
            : DecimalsOf(step);

        Span<char> text = stackalloc char[TEXT_BUFFER];
        var labelWidth = fonts.AxisStrong.Measure(text[..ChartFormat.Price(text, last.Close, priceDecimals)]);
        for (var i = 0; i < ticks; i++)
        {
            var len = ChartFormat.Price(text, firstTick + (i * step), axisDecimals);
            labelWidth = Math.Max(labelWidth, fonts.Axis.Measure(text[..len]));
        }

        var plotRight = WIDTH - Math.Max(MIN_AXIS_WIDTH, (int)MathF.Ceiling(labelWidth) + (2 * AXIS_PAD));
        var slot = (double)plotRight / (candles.Length + RIGHT_SLOTS);
        var wick = slot >= WIDE_WICK_SLOT ? 2 : 1;
        var body = Math.Max(wick, (int)(slot * BODY_RATIO));
        if (((body - wick) & 1) == 1)
            body--;

        var layout = new CandleLayout(slot, body, wick);

        Span<int> labelIndex = stackalloc int[MAX_TIME_LABELS];
        Span<int> labelLeft = stackalloc int[MAX_TIME_LABELS];
        Span<int> labelRight = stackalloc int[MAX_TIME_LABELS];
        var labels = PlaceTimeLabelsInternal(candles, layout, fonts, plotRight, text, labelIndex, labelLeft, labelRight);

        canvas.Clear(BACKGROUND);

        for (var i = 0; i < ticks; i++)
        {
            var y = scale.Y(firstTick + (i * step));
            canvas.FillRect(0, y, plotRight, y + 1, GRID);
        }

        for (var i = 0; i < labels; i++)
        {
            var x = layout.Center(labelIndex[i]);
            canvas.FillRect(x, PLOT_TOP, x + 1, PLOT_BOTTOM, GRID);
        }

        var watermark = fonts.Watermark;
        var watermarkWidth = watermark.Measure(header.Symbol);
        if (watermarkWidth < plotRight - (4 * SIDE_PAD))
        {
            canvas.DrawText(watermark,
                header.Symbol,
                (plotRight - watermarkWidth) / 2,
                (PLOT_TOP + PLOT_BOTTOM + watermark.CapHeight) / 2,
                TEXT,
                WATERMARK_ALPHA);
        }

        for (var i = 0; i < candles.Length; i++)
        {
            ref readonly var c = ref candles[i];
            if (!(maxVolume > 0) || !(c.Volume > 0))
                continue;

            var height = (int)Math.Round(c.Volume / maxVolume * VOLUME_HEIGHT);
            var left = layout.Left(i);
            canvas.BlendRect(left, PLOT_BOTTOM - height, left + body, PLOT_BOTTOM, c.Close >= c.Open ? UP : DOWN, VOLUME_ALPHA);
        }

        for (var i = 0; i < candles.Length; i++)
        {
            ref readonly var c = ref candles[i];
            var color = c.Close >= c.Open ? UP : DOWN;
            var left = layout.Left(i);
            var wickLeft = left + ((body - wick) / 2);
            canvas.FillRect(wickLeft, scale.Y(c.High), wickLeft + wick, scale.Y(c.Low) + 1, color);

            var top = scale.Y(Math.Max(c.Open, c.Close));
            var bottom = scale.Y(Math.Min(c.Open, c.Close));
            if (bottom - top < MIN_BODY_HEIGHT)
            {
                top = ((top + bottom) / 2) - (MIN_BODY_HEIGHT / 2);
                bottom = top + MIN_BODY_HEIGHT;
            }

            canvas.FillRect(left, top, left + body, bottom, color);
        }

        canvas.DashedHLine(0, plotRight, lastY - (LAST_LINE_HEIGHT / 2), LAST_LINE_HEIGHT, DASH_LENGTH, DASH_LENGTH, lastColor);

        canvas.FillRect(plotRight, PLOT_TOP, plotRight + 1, HEIGHT, BORDER);
        canvas.FillRect(0, PLOT_BOTTOM, WIDTH, PLOT_BOTTOM + 1, BORDER);

        var tagHalf = (fonts.AxisStrong.CapHeight / 2) + TAG_PAD;
        var axisCap = fonts.Axis.CapHeight;
        for (var i = 0; i < ticks; i++)
        {
            var y = scale.Y(firstTick + (i * step));
            if (Math.Abs(y - lastY) < tagHalf + axisCap)
                continue;

            var len = ChartFormat.Price(text, firstTick + (i * step), axisDecimals);
            canvas.DrawText(fonts.Axis, text[..len], plotRight + AXIS_PAD, y + (axisCap / 2), TEXT_SOFT);
        }

        canvas.FillRoundedRect(plotRight + 1, lastY - tagHalf, WIDTH - TAG_RIGHT_MARGIN, lastY + tagHalf, TAG_RADIUS, lastColor);
        var tagLength = ChartFormat.Price(text, last.Close, priceDecimals);
        canvas.DrawText(fonts.AxisStrong,
            text[..tagLength],
            plotRight + AXIS_PAD,
            lastY + (fonts.AxisStrong.CapHeight / 2),
            TAG_TEXT);

        var timeBaseline = PLOT_BOTTOM + ((HEIGHT - PLOT_BOTTOM + axisCap) / 2);
        for (var i = 0; i < labels; i++)
        {
            var index = labelIndex[i];
            var major = IsNewMonth(candles, index);
            var len = TimeLabel(text, DateOf(candles[index].Time), major);
            canvas.DrawText(major ? fonts.AxisStrong : fonts.Axis, text[..len], labelLeft[i], timeBaseline, TEXT_SOFT);
        }

        DrawHeaderInternal(canvas, candles, header, fonts, priceDecimals, text);
    }

    // Month labels first, then the first candle of each week, where they do not touch a placed label.
    private static int PlaceTimeLabelsInternal(
        ReadOnlySpan<Candle> candles,
        in CandleLayout layout,
        ChartFonts fonts,
        int plotRight,
        Span<char> text,
        Span<int> labelIndex,
        Span<int> labelLeft,
        Span<int> labelRight)
    {
        var labels = 0;
        for (var pass = 0; pass < 2; pass++)
        {
            var major = pass == 0;
            for (var i = 1; i < candles.Length && labels < labelIndex.Length; i++)
            {
                var newMonth = IsNewMonth(candles, i);
                if (major
                        ? !newMonth
                        : newMonth || WeekOf(candles[i].Time) == WeekOf(candles[i - 1].Time))
                    continue;

                var font = major ? fonts.AxisStrong : fonts.Axis;
                var width = (int)MathF.Ceiling(font.Measure(text[..TimeLabel(text, DateOf(candles[i].Time), major)]));
                var left = layout.Center(i) - (width / 2);
                var right = left + width;
                if (left < SIDE_PAD / 2 || right > plotRight - AXIS_PAD)
                    continue;

                var free = true;
                for (var k = 0; k < labels && free; k++)
                    free = right + TIME_LABEL_GAP <= labelLeft[k] || left - TIME_LABEL_GAP >= labelRight[k];

                if (!free)
                    continue;

                labelIndex[labels] = i;
                labelLeft[labels] = left;
                labelRight[labels] = right;
                labels++;
            }
        }

        return labels;
    }

    private static void DrawHeaderInternal(
        ChartCanvas canvas,
        ReadOnlySpan<Candle> candles,
        in ChartHeader header,
        ChartFonts fonts,
        int decimals,
        Span<char> text)
    {
        ref readonly var last = ref candles[^1];
        var previous = candles.Length > 1 ? candles[^2].Close : last.Open;
        var change = last.Close - previous;
        var barColor = last.Close >= last.Open ? UP : DOWN;

        var priceLength = ChartFormat.Price(text, last.Close, decimals);
        var priceWidth = fonts.Symbol.Measure(text[..priceLength]);
        var changeText = text[priceLength..];
        var changeLength = ChartFormat.Change(changeText, change, previous > 0 ? change / previous * 100 : 0, decimals);
        var changeX = WIDTH - SIDE_PAD - fonts.Title.Measure(changeText[..changeLength]);
        var priceX = changeX - PRICE_GAP - priceWidth;
        canvas.DrawText(fonts.Symbol, text[..priceLength], priceX, TITLE_BASELINE, TEXT);
        canvas.DrawText(fonts.Title, changeText[..changeLength], changeX, TITLE_BASELINE, change >= 0 ? UP : DOWN);

        var x = canvas.DrawText(fonts.Symbol, header.Symbol, SIDE_PAD, TITLE_BASELINE, TEXT) + SYMBOL_GAP;

        Span<char> line = stackalloc char[LINE_BUFFER];
        var suffix = 0;
        AppendInternal(line, ref suffix, INTERVAL);
        if (!string.IsNullOrEmpty(header.Venue))
        {
            AppendInternal(line, ref suffix, SEPARATOR);
            AppendInternal(line, ref suffix, header.Venue);
        }

        var title = fonts.Title;
        var room = priceX - NAME_PRICE_GAP - x - title.Measure(line[..suffix]);
        var name = header.Name.AsSpan().Trim();
        if (!name.IsEmpty && !name.ContainsAnyExceptInRange(' ', '~') && room > title.Measure(ELLIPSIS + SEPARATOR))
        {
            room -= title.Measure(SEPARATOR);
            if (title.Measure(name) > room)
            {
                room -= title.Measure(ELLIPSIS);
                var fit = 0;
                var width = 0f;
                while (fit < name.Length && width + title.Advance(name[fit]) <= room)
                    width += title.Advance(name[fit++]);

                name = name[..fit].TrimEnd();
                x = canvas.DrawText(title, name, x, TITLE_BASELINE, TEXT_SOFT);
                x = canvas.DrawText(title, ELLIPSIS, x, TITLE_BASELINE, TEXT_SOFT);
            }
            else
            {
                x = canvas.DrawText(title, name, x, TITLE_BASELINE, TEXT_SOFT);
            }

            x = canvas.DrawText(title, SEPARATOR, x, TITLE_BASELINE, TEXT_SOFT);
        }

        canvas.DrawText(title, line[..suffix], x, TITLE_BASELINE, TEXT_SOFT);

        x = SIDE_PAD;
        x = DrawLegendInternal(canvas, fonts.Legend, "O", text[..ChartFormat.Price(text, last.Open, decimals)], x, barColor);
        x = DrawLegendInternal(canvas, fonts.Legend, "H", text[..ChartFormat.Price(text, last.High, decimals)], x, barColor);
        x = DrawLegendInternal(canvas, fonts.Legend, "L", text[..ChartFormat.Price(text, last.Low, decimals)], x, barColor);
        x = DrawLegendInternal(canvas, fonts.Legend, "C", text[..ChartFormat.Price(text, last.Close, decimals)], x, barColor);
        if (last.Volume > 0)
            DrawLegendInternal(canvas, fonts.Legend, "Vol", text[..ChartFormat.Short(text, last.Volume)], x, barColor);
    }

    private static float DrawLegendInternal(
        ChartCanvas canvas,
        ChartFont font,
        ReadOnlySpan<char> label,
        ReadOnlySpan<char> value,
        float x,
        uint color)
    {
        x = canvas.DrawText(font, label, x, LEGEND_BASELINE, TEXT_DIM) + LEGEND_LABEL_GAP;
        return canvas.DrawText(font, value, x, LEGEND_BASELINE, color) + LEGEND_GAP;
    }

    private static void AppendInternal(Span<char> line, ref int length, ReadOnlySpan<char> part)
    {
        var count = Math.Min(part.Length, line.Length - length);
        part[..count].CopyTo(line[length..]);
        length += count;
    }

    private static double NiceStep(double raw)
    {
        if (!(raw > 0) || !double.IsFinite(raw))
            return 1;

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var normalized = raw / magnitude;
        var nice = normalized switch
        {
            <= 1 => 1,
            <= 2 => 2,
            <= 2.5 => 2.5,
            <= 5 => 5,
            _ => 10
        };

        return nice * magnitude;
    }

    private static int DecimalsOf(double step)
    {
        for (var decimals = 0; decimals < ChartFormat.MAX_DECIMALS; decimals++)
        {
            var scaled = step * Math.Pow(10, decimals);
            if (Math.Abs(scaled - Math.Round(scaled)) < scaled * 1e-6)
                return decimals;
        }

        return ChartFormat.MAX_DECIMALS;
    }

    private static DateOnly DateOf(long time)
        => DateOnly.FromDayNumber(_epochDay + (int)(time / SECONDS_PER_DAY));

    // Weeks start on Monday. Day 0 (1970-01-01) is a Thursday.
    private static long WeekOf(long time)
        => ((time / SECONDS_PER_DAY) + 3) / 7;

    private static bool IsNewMonth(ReadOnlySpan<Candle> candles, int index)
    {
        if (index == 0)
            return false;

        var date = DateOf(candles[index].Time);
        var previous = DateOf(candles[index - 1].Time);
        return date.Month != previous.Month || date.Year != previous.Year;
    }

    private static int TimeLabel(Span<char> dst, DateOnly date, bool major)
    {
        if (!major)
            return date.Day.TryFormat(dst, out var day, provider: CultureInfo.InvariantCulture) ? day : 0;

        if (date.Month == 1)
            return date.Year.TryFormat(dst, out var year, provider: CultureInfo.InvariantCulture) ? year : 0;

        var month = _months[date.Month - 1];
        month.CopyTo(dst);
        return month.Length;
    }
}
