namespace Nadeko.Econ;

public abstract class NewDeck<TCard, TSuit, TValue> 
    where TCard: NewCard<TSuit, TValue>
    where TSuit : struct, Enum
    where TValue : struct, Enum
{
    protected static readonly TSuit[] _suits = Enum.GetValues<TSuit>();
    protected static readonly TValue[] _values = Enum.GetValues<TValue>();
    
    public virtual int CurrentCount
        => _cards.Count;
    
    public virtual int TotalCount { get; }

    protected readonly LinkedList<TCard> _cards = new();
    public NewDeck()
    {
        TotalCount = _suits.Length * _values.Length;
    }

    public virtual TCard? Draw()
    {
        var first = _cards.First;
        if (first is not null)
        {
            _cards.RemoveFirst();
            return first.Value;
        }

        return null;
    }

    public virtual TCard? Peek(int x = 0)
    {
        var card = _cards.First;
        for (var i = 0; i < x; i++)
        {
            card = card?.Next;
        }

        return card?.Value;
    }

    public virtual void Shuffle()
    {
        var cards = _cards.ToList();
        var newCards = cards.Shuffle();
        
        _cards.Clear();
        foreach (var card in newCards)
            _cards.AddFirst(card);
    }
}