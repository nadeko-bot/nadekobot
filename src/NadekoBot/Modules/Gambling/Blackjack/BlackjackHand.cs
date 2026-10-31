namespace NadekoBot.Modules.Gambling.Blackjack;

public readonly record struct BjHandValue(int Total, bool IsSoft)
{
    public bool IsBust
        => Total > BlackjackHand.BLACKJACK;
}

public enum BjHandResult : byte
{
    None,
    Blackjack,
    Win,
    Push,
    Lose,
    Bust
}

public sealed class BlackjackHand(long bet)
{
    public const int BLACKJACK = 21;
    private const int SOFT_BONUS = 10;

    // Every card is worth at least 1 point, so a hand stops at 21 cards at the latest.
    private const int MAX_CARDS = BLACKJACK;

    private readonly BjCard[] _cards = new BjCard[MAX_CARDS];

    public int Count { get; private set; }
    public long BaseBet { get; } = bet;
    public long Bet { get; private set; } = bet;
    public bool IsDoubled { get; private set; }
    public bool IsFromSplit { get; private set; }
    public bool IsStood { get; private set; }
    public BjHandResult Result { get; private set; }
    public long Payout { get; private set; }

    public ReadOnlySpan<BjCard> Cards
        => _cards.AsSpan(0, Count);

    public BjHandValue Value
        => Evaluate(Cards);

    public bool IsNatural
        => !IsFromSplit && Count == 2 && Value.Total == BLACKJACK;

    public bool IsBust
        => Value.IsBust;

    public bool IsFinished
        => IsStood || Value.Total >= BLACKJACK;

    public static BjHandValue Evaluate(ReadOnlySpan<BjCard> cards)
    {
        var total = 0;
        var hasAce = false;
        foreach (var card in cards)
        {
            total += card.Points;
            hasAce |= card.IsAce;
        }

        return hasAce && total + SOFT_BONUS <= BLACKJACK
            ? new(total + SOFT_BONUS, true)
            : new(total, false);
    }

    public void Add(BjCard card)
    {
        if (Count == MAX_CARDS)
            throw new InvalidOperationException("A blackjack hand cannot take more cards.");

        _cards[Count++] = card;
    }

    public BjCard RemoveSecond()
    {
        if (Count != 2)
            throw new InvalidOperationException("Only a hand with two cards can split.");

        Count = 1;
        return _cards[1];
    }

    // a split ace gets one card
    public void MarkSplit(bool aces)
    {
        IsFromSplit = true;
        IsStood |= aces;
    }

    public void Double()
    {
        IsDoubled = true;
        Bet += Bet;
    }

    public void Stand()
        => IsStood = true;

    public void SetResult(BjHandResult result, long payout)
    {
        Result = result;
        Payout = payout;
    }

    // Rebuilds a hand from a saved round, in the state which a played hand reaches.
    public static BlackjackHand Restore(
        ReadOnlySpan<BjCard> cards,
        long baseBet,
        bool isDoubled,
        bool isFromSplit,
        bool isStood)
    {
        var hand = new BlackjackHand(baseBet);
        foreach (var card in cards)
            hand.Add(card);

        hand.IsDoubled = isDoubled;
        if (isDoubled)
            hand.Bet = baseBet * 2;
        hand.IsFromSplit = isFromSplit;
        hand.IsStood = isStood;
        return hand;
    }
}
