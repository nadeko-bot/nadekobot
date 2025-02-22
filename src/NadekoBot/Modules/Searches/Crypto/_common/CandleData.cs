namespace NadekoBot.Modules.Searches;

public record CandleData(
    decimal Open,
    decimal Close,
    decimal High,
    decimal Low,
    long Volume);