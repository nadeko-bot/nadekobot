using SixLabors.ImageSharp;

namespace NadekoBot.Modules.Searches;

/// <summary>
/// All data required to draw a candle
/// </summary>
/// <param name="IsGreen">Whether the candle is green</param>
/// <param name="BodyRect">Rectangle for the body</param>
/// <param name="High">High line point</param>
/// <param name="Low">Low line point</param>
public record CandleDrawingData(bool IsGreen, RectangleF BodyRect, PointF High, PointF Low);