namespace NadekoBot.Modules.Searches.Charts;

// Time is the unix start in seconds, shown as UTC: 00:00 of the date for a daily candle, the New York clock for a shorter stock candle.
public readonly record struct Candle(long Time, double Open, double High, double Low, double Close, double Volume);
