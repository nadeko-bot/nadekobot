using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Runtime.CompilerServices;
using Color = SixLabors.ImageSharp.Color;

namespace NadekoBot.Modules.Searches;

public sealed class ImagesharpStockChartDrawingService : IStockChartDrawingService, INService
{
    private const int WIDTH = 300;
    private const int HEIGHT = 100;
    private const decimal MAX_HEIGHT = HEIGHT * 0.8m;

    private static readonly Rgba32 _backgroundColor = Rgba32.ParseHex("17181E");
    private static readonly Rgba32 _lineGuideColor = Rgba32.ParseHex("212125");
    private static readonly Rgba32 _sparklineColor = Rgba32.ParseHex("2961FC");
    private static readonly Rgba32 _greenBrush = Rgba32.ParseHex("26A69A");
    private static readonly Rgba32 _redBrush = Rgba32.ParseHex("EF5350");

    private static float GetNormalizedPoint(decimal max, decimal point, decimal range)
        => (float)((MAX_HEIGHT * ((max - point) / range)) + HeightOffset());
        
    private PointF[] GetSparklinePointsInternal(IReadOnlyCollection<CandleData> series)
    {
        var candleStep = WIDTH / (series.Count + 1);
        var max = series.Max(static x => x.High);
        var min = series.Min(static x => x.Low);
    
        var range = max - min;
    
        var points = new PointF[series.Count];
    
        var i = 0;
        foreach (var candle in series)
        {
            var x = candleStep * (i + 1);

            var y = GetNormalizedPoint(max, candle.Close, range);
            points[i++] = new(x, y);
        }

        return points;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static decimal HeightOffset()
        => (HEIGHT - MAX_HEIGHT) / 2m;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Image<Rgba32> CreateCanvasInternal()
        => new Image<Rgba32>(WIDTH, HEIGHT, _backgroundColor);

    private CandleDrawingData[] GetChartDrawingDataInternal(IReadOnlyCollection<CandleData> series)
    {
        var candleMargin = 2;
        var candleStep = (WIDTH - (candleMargin * series.Count)) / (series.Count + 1);
        var max = series.Max(static x => x.High);
        var min = series.Min(static x => x.Low);

        var range = max - min;

        var drawData = new CandleDrawingData[series.Count];

        var candleWidth = candleStep;
        
        var i = 0;
        foreach (var candle in series)
        {
            var offsetX = (i - 1) * candleMargin; 
            var x = (candleStep * (i + 1)) + offsetX;
            var yOpen = GetNormalizedPoint(max, candle.Open, range);
            var yClose = GetNormalizedPoint(max, candle.Close, range);
            var y = candle.Open > candle.Close
                ? yOpen
                : yClose;

            var sizeH = Math.Abs(yOpen - yClose);

            var high = GetNormalizedPoint(max, candle.High, range);
            var low = GetNormalizedPoint(max, candle.Low, range);
            drawData[i] = new(candle.Open < candle.Close,
                new(x, y, candleWidth, sizeH),
                new(x + (candleStep / 2), high),
                new(x + (candleStep / 2), low));
            ++i;
        }

        return drawData;
    }

    private void DrawChartData(Image<Rgba32> image, CandleDrawingData[] drawData)
        => image.Mutate(ctx =>
        {
            foreach (var data in drawData)
                ctx.DrawLine(data.IsGreen
                        ? _greenBrush
                        : _redBrush,
                    1,
                    data.High,
                    data.Low);


            foreach (var data in drawData)
                ctx.Fill(data.IsGreen
                        ? _greenBrush
                        : _redBrush,
                    data.BodyRect);
        });

    private void DrawLineGuides(Image<Rgba32> image, IReadOnlyCollection<CandleData> series)
    {
        var max = series.Max(x => x.High);
        var min = series.Min(x => x.Low);

        var step = (max - min) / 5;

        var lines = new float[6];
        
        for (var i = 0; i < 6; i++)
        {
            var y = GetNormalizedPoint(max, min + (step * i), max - min);
            lines[i] = y;
        }

        image.Mutate(ctx =>
        {
            // draw guides
            foreach (var y in lines)
                ctx.DrawLine(_lineGuideColor, 1, new PointF(0, y), new PointF(WIDTH, y));
            
            // // draw min and max price on the chart
            // ctx.DrawText(min.ToString(CultureInfo.InvariantCulture),
            //     SystemFonts.CreateFont("Arial", 5),
            //     Color.White,
            //     new PointF(0, (float)HeightOffset() - 5)
            // );
            //
            // ctx.DrawText(max.ToString("N1", CultureInfo.InvariantCulture),
            //     SystemFonts.CreateFont("Arial", 5),
            //     Color.White,
            //     new PointF(0,  HEIGHT - (float)HeightOffset())
            // );
        });
    }
    
    public Task<ImageData?> GenerateSparklineAsync(IReadOnlyCollection<CandleData> series)
    {
        if (series.Count == 0)
            return Task.FromResult<ImageData?>(default);

        using var image = CreateCanvasInternal();

        var points = GetSparklinePointsInternal(series);
        
        image.Mutate(ctx =>
        {
            ctx.DrawLine(_sparklineColor, 2, points);
        });
    
        return Task.FromResult<ImageData?>(new("png", image.ToStream()));
    }

    public Task<ImageData?> GenerateCombinedChartAsync(IReadOnlyCollection<CandleData> series)
    {
        if (series.Count == 0)
            return Task.FromResult<ImageData?>(default);

        using var image = CreateCanvasInternal();
        
        DrawLineGuides(image, series);
        
        var chartData = GetChartDrawingDataInternal(series);
        DrawChartData(image, chartData);

        var points = GetSparklinePointsInternal(series);
        image.Mutate(ctx =>
        {
            ctx.DrawLine(Color.ParseHex("00FFFFAA"), 1, points);
        });

        return Task.FromResult<ImageData?>(new("png", image.ToStream()));
    }
    
    public Task<ImageData?> GenerateCandleChartAsync(IReadOnlyCollection<CandleData> series)
    {
        if (series.Count == 0)
            return Task.FromResult<ImageData?>(default);

        using var image = CreateCanvasInternal();

        DrawLineGuides(image, series);
        
        var drawData = GetChartDrawingDataInternal(series);
        DrawChartData(image, drawData);

        return Task.FromResult<ImageData?>(new("png", image.ToStream()));
    }
}