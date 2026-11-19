namespace NadekoBot.Modules.Gambling.Common.AnimalRacing;

public sealed class RaceSave
{
    public const int VERSION = 1;

    public int V { get; init; }
    public int StartTime { get; init; }

    // in the order of .race; the member at join position i races as animal i
    public RaceAnimalSave[]? Animals { get; init; }

    public bool Running { get; init; }
}

public sealed class RaceAnimalSave
{
    public string? Icon { get; init; }
    public string? Name { get; init; }
}
