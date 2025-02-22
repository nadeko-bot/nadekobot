using Nadeko.Econ;

namespace NadekoBot.Modules.Gambling.Common;

public class QuadDeck : Deck
{
    protected override void RefillPool()
    {
        CardPool = new(52 * 4);
        for (var j = 1; j < 14; j++)
        for (var i = 1; i < 5; i++)
        {
            CardPool.Add(new((CardSuit)i, j));
            CardPool.Add(new((CardSuit)i, j));
            CardPool.Add(new((CardSuit)i, j));
            CardPool.Add(new((CardSuit)i, j));
        }
    }
}