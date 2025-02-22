namespace NadekoBot.Modules.Gambling;

public readonly struct FlipResult
{
    public long Won { get; init; }
    public int Side { get; init; }
}