namespace Nadeko.Econ;

public sealed class RegularDeck : NewDeck<RegularCard, RegularSuit, RegularValue>
{
    public RegularDeck()
    {
        foreach (var suit in _suits)
        {
            foreach (var val in _values)
            {
                _cards.AddLast((RegularCard)Activator.CreateInstance(typeof(RegularCard), suit, val)!);
            }
        }
    }
}