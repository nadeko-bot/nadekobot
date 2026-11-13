namespace NadekoBot.Modules.Searches.Charts;

// Time is the unix time in seconds of the start of the candle. A daily candle starts at 00:00 UTC of its date.
public readonly record struct Candle(long Time, double Open, double High, double Low, double Close, double Volume);
