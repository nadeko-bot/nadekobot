namespace NadekoBot.Modules.Gambling;

public sealed class BetrollGame
{
    private readonly (int WhenAbove, decimal MultiplyBy)[] _thresholdPairs;
    private readonly NadekoRandom _rng;

    public BetrollGame(IReadOnlyList<(int WhenAbove, decimal MultiplyBy)> pairs)
    {
        _thresholdPairs = pairs.OrderByDescending(x => x.WhenAbove).ToArray();
        _rng = new();
    }

    public BetrollResult Roll(decimal amount = 0)
    {
        var roll = _rng.Next(1, 101);

        for (var i = 0; i < _thresholdPairs.Length; i++)
        {
            ref var pair = ref _thresholdPairs[i];

            if (pair.WhenAbove < roll)
            {
                return new()
                {
                    Multiplier = pair.MultiplyBy,
                    Roll = roll,
                    Threshold = pair.WhenAbove,
                    Won = amount * pair.MultiplyBy
                };
            }
        }

        return new()
        {
            Multiplier = 0,
            Roll = roll,
            Threshold = -1,
            Won = 0,
        };
    }
}