namespace NadekoBot.Modules.Gambling.Blackjack;

public readonly record struct BjCard(byte Rank, byte Suit)
{
    public const byte ACE = 1;
    public const byte KING = 13;
    public const byte SUITS = 4;

    public int Points
        => Rank >= 10 ? 10 : Rank;

    public bool IsAce
        => Rank == ACE;

    public bool IsTenValue
        => Rank >= 10;
}

public sealed class BlackjackShoe
{
    public const int DECKS = 6;
    public const int CARDS_PER_DECK = 52;
    public const int SIZE = DECKS * CARDS_PER_DECK;

    private readonly BjCard[] _cards;
    private int _next;

    private BlackjackShoe(BjCard[] cards)
        => _cards = cards;

    public int Remaining
        => _cards.Length - _next;

    // The cards the round would draw next, which a saved round needs to play out the same way.
    public ReadOnlySpan<BjCard> RemainingCards
        => _cards.AsSpan(_next);

    public static BlackjackShoe CreateShuffled(NadekoRandom rng)
    {
        var cards = new BjCard[SIZE];
        var i = 0;
        for (var deck = 0; deck < DECKS; deck++)
        for (byte suit = 0; suit < BjCard.SUITS; suit++)
        for (byte rank = BjCard.ACE; rank <= BjCard.KING; rank++)
            cards[i++] = new(rank, suit);

        for (var n = cards.Length - 1; n > 0; n--)
        {
            var k = rng.Next(n + 1);
            (cards[n], cards[k]) = (cards[k], cards[n]);
        }

        return new(cards);
    }

    public static BlackjackShoe FromOrder(ReadOnlySpan<BjCard> order)
        => new(order.ToArray());

    // A round cannot empty the shoe: each of at most 11 hands stops below 31 points,
    // and 330 points are at most 114 cards even when the bot draws the smallest cards first.
    public BjCard Draw()
    {
        if (_next >= _cards.Length)
            throw new InvalidOperationException("The blackjack shoe has no cards left.");

        return _cards[_next++];
    }
}
