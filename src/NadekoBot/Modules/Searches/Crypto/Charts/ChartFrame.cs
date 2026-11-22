namespace NadekoBot.Modules.Searches.Charts;

// The time of one candle. Day is 0, so a cached quote without a frame reads as daily.
public enum ChartFrame
{
    Day,
    M1,
    M5,
    M15,
    M30,
    H1,
    H4
}

public static class ChartFrameExtensions
{
    public static int Seconds(this ChartFrame frame)
        => frame switch
        {
            ChartFrame.M1 => 60,
            ChartFrame.M5 => 300,
            ChartFrame.M15 => 900,
            ChartFrame.M30 => 1_800,
            ChartFrame.H1 => 3_600,
            ChartFrame.H4 => 14_400,
            _ => 86_400
        };

    public static string Label(this ChartFrame frame)
        => frame switch
        {
            ChartFrame.M1 => "1m",
            ChartFrame.M5 => "5m",
            ChartFrame.M15 => "15m",
            ChartFrame.M30 => "30m",
            ChartFrame.H1 => "1h",
            ChartFrame.H4 => "4h",
            _ => "1D"
        };
}
