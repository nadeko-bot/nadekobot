namespace NadekoBot.Modules.Gambling.Rps;

public sealed class RpsGame
{
    private static readonly NadekoRandom _rng = new NadekoRandom();

    private const decimal WIN_MULTI = 1.95m;
    private const decimal DRAW_MULTI = 1m;
    private const decimal LOSE_MULTI = 0m;
    
    public RpsGame()
    {
        
    }
    
    public RpsResult Play(RpsPick pick, decimal amount)
    {
        var compPick = (RpsPick)_rng.Next(0, 3);
        if (compPick == pick)
        {
            return new()
            {
                Won = amount * DRAW_MULTI,
                Multiplier = DRAW_MULTI,
                ComputerPick = compPick,
                Result = RpsResultType.Draw,
            };
        }

        if ((compPick == RpsPick.Paper && pick == RpsPick.Rock)
            || (compPick == RpsPick.Rock && pick == RpsPick.Scissors)
            || (compPick == RpsPick.Scissors && pick == RpsPick.Paper))
        {
            return new()
            {
                Won = amount * LOSE_MULTI,
                Multiplier = LOSE_MULTI,
                Result = RpsResultType.Lose,
                ComputerPick = compPick,
            };
        }

        return new()
        {
            Won = amount * WIN_MULTI,
            Multiplier = WIN_MULTI,
            Result = RpsResultType.Win,
            ComputerPick = compPick,
        };
    }
}

public enum RpsPick : byte
{
    Rock = 0,
    Paper = 1,
    Scissors = 2,
}

public enum RpsResultType : byte
{
    Win,
    Draw,
    Lose
}



public readonly struct RpsResult
{
    public decimal Won { get; init; }
    public decimal Multiplier { get; init; }
    public RpsResultType Result { get; init; }
    public RpsPick ComputerPick { get; init; }
}