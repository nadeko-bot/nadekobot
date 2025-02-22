namespace NadekoBot.Modules.Gambling;

public readonly struct SlotResult
{
    public decimal Multiplier { get; init; }
    public byte[] Rolls { get; init; }
    public decimal Won { get; init; }
    public SlotWinType WinType { get; init; }
}