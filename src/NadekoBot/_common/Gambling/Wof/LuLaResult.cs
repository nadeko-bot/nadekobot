namespace NadekoBot.Modules.Gambling;

public readonly struct LuLaResult
{
    public int Index { get; init; }
    public decimal Multiplier { get; init; }
    public decimal Won { get; init; }
    public IReadOnlyList<decimal> Multipliers { get; init; }
}