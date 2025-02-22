namespace NadekoBot.Modules.Gambling;

public readonly struct BetrollResult
{
    public int Roll { get; init; }
    public decimal Multiplier { get; init; }
    public decimal Threshold { get; init; }
    public decimal Won { get; init; }
}