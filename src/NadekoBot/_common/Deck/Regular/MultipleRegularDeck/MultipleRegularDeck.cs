namespace Nadeko.Econ;

public class MultipleRegularDeck : NewDeck<RegularCard, RegularSuit, RegularValue>
{
    private int Decks { get; }

    public override int TotalCount { get; }

    public MultipleRegularDeck(int decks = 1)
    {
        if (decks < 1)
            throw new ArgumentOutOfRangeException(nameof(decks), "Has to be more than 0");

        Decks = decks;
        TotalCount = base.TotalCount * decks;
        
        for (var i = 0; i < Decks; i++)
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
}