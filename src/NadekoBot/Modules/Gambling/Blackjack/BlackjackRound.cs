namespace NadekoBot.Modules.Gambling.Blackjack;

public enum BjRoundPhase : byte
{
    Dealing,
    Players,
    Finished
}

public readonly record struct BjSeat(ulong UserId, long Bet);

public readonly record struct BlackjackPayout(string Extra, long Amount);

public sealed class BlackjackPlayer
{
    public ulong UserId { get; }
    public List<BlackjackHand> Hands { get; }

    public BlackjackPlayer(ulong userId, long bet)
    {
        UserId = userId;
        Hands = new(BlackjackRound.MAX_HANDS) { new(bet) };
    }

    public long TotalStake
    {
        get
        {
            long total = 0;
            foreach (var hand in Hands)
                total += hand.Bet;
            return total;
        }
    }
}

// Game rules only. The caller takes the currency for a double or a split before it calls the action.
// European rules: the dealer gets the second card only after all players finish, and hits soft 17.
public sealed class BlackjackRound
{
    // a player splits at most once
    public const int MAX_HANDS = 2;
    public const int DEALER_STANDS_AT = 17;

    // a member doubles only on 9, 10 or 11, which keeps the house edge at about 1%
    public const int DOUBLE_ON_MIN = 9;
    public const int DOUBLE_ON_MAX = 11;

    private readonly BlackjackShoe _shoe;
    private readonly BlackjackPlayer[] _players;

    public BlackjackRound(BlackjackShoe shoe, ReadOnlySpan<BjSeat> seats)
    {
        _shoe = shoe;
        _players = new BlackjackPlayer[seats.Length];
        for (var i = 0; i < seats.Length; i++)
            _players[i] = new(seats[i].UserId, seats[i].Bet);
    }

    private BlackjackRound(
        BlackjackShoe shoe,
        BlackjackPlayer[] players,
        ReadOnlySpan<BjCard> dealerCards,
        BjRoundPhase phase,
        int playerIndex,
        int handIndex)
    {
        _shoe = shoe;
        _players = players;
        foreach (var card in dealerCards)
            Dealer.Add(card);

        Phase = phase;
        PlayerIndex = playerIndex;
        HandIndex = handIndex;
    }

    public static BlackjackRound Restore(
        BlackjackShoe shoe,
        BlackjackPlayer[] players,
        ReadOnlySpan<BjCard> dealerCards,
        BjRoundPhase phase,
        int playerIndex,
        int handIndex)
        => new(shoe, players, dealerCards, phase, playerIndex, handIndex);

    // A saved round which a restart interrupted: open hands stand and the dealer plays from the saved shoe.
    // When the dealer already drew, the saved cards give every result.
    public void FinishRestored()
    {
        if (Phase == BjRoundPhase.Finished)
        {
            FinishInternal();
            return;
        }

        Finish();
    }

    public BlackjackHand Dealer { get; } = new(0);
    public BjRoundPhase Phase { get; private set; } = BjRoundPhase.Dealing;
    public int PlayerIndex { get; private set; }
    public int HandIndex { get; private set; }

    public bool DealerHasBlackjack
        => Dealer.IsNatural;

    // With an up card from 2 to 9 the dealer's second card cannot make a blackjack.
    public bool DealerCanHaveBlackjack
        => Dealer.Count > 0 && (Dealer.Cards[0].IsAce || Dealer.Cards[0].IsTenValue);

    public ReadOnlySpan<BlackjackPlayer> Players
        => _players;

    public ReadOnlySpan<BjCard> ShoeRemaining
        => _shoe.RemainingCards;

    public BlackjackPlayer? CurrentPlayer
        => Phase == BjRoundPhase.Players ? _players[PlayerIndex] : null;

    public BlackjackHand? CurrentHand
        => CurrentPlayer?.Hands[HandIndex];

    public bool CanHit
        => CurrentHand is { IsFinished: false };

    public bool CanDouble
        => CurrentHand is { Count: 2, IsDoubled: false, IsFromSplit: false, IsFinished: false } hand
           && hand.Value.Total is >= DOUBLE_ON_MIN and <= DOUBLE_ON_MAX;

    public bool CanSplit
        => CurrentHand is { Count: 2, IsFinished: false } hand
           && hand.Cards[0].Rank == hand.Cards[1].Rank
           && CurrentPlayer!.Hands.Count < MAX_HANDS;

    public long DoubleCost
        => CurrentHand?.Bet ?? 0;

    public long SplitCost
        => CurrentHand?.BaseBet ?? 0;

    public void Deal()
    {
        if (Phase != BjRoundPhase.Dealing)
            throw new InvalidOperationException("The cards are already dealt.");

        foreach (var player in _players)
            player.Hands[0].Add(_shoe.Draw());

        Dealer.Add(_shoe.Draw());

        foreach (var player in _players)
            player.Hands[0].Add(_shoe.Draw());

        Phase = BjRoundPhase.Players;
        PlayerIndex = 0;
        HandIndex = 0;
        NextHandInternal();
    }

    public void Hit()
    {
        var hand = RequireInternal(CanHit);
        hand.Add(_shoe.Draw());
        if (hand.IsFinished)
            NextHandInternal();
    }

    public void Stand()
    {
        RequireInternal(CanHit).Stand();
        NextHandInternal();
    }

    public void Double()
    {
        var hand = RequireInternal(CanDouble);
        hand.Double();
        hand.Add(_shoe.Draw());
        hand.Stand();
        NextHandInternal();
    }

    public void Split()
    {
        var hand = RequireInternal(CanSplit);
        var second = hand.RemoveSecond();
        var aces = second.IsAce;

        var other = new BlackjackHand(hand.BaseBet);
        other.Add(second);
        hand.Add(_shoe.Draw());
        other.Add(_shoe.Draw());
        hand.MarkSplit(aces);
        other.MarkSplit(aces);

        CurrentPlayer!.Hands.Insert(HandIndex + 1, other);

        if (hand.IsFinished)
            NextHandInternal();
    }

    // The table cannot show itself any more: the players cannot act, so every open hand stands and the dealer plays.
    public void Finish()
    {
        if (Phase == BjRoundPhase.Finished)
            return;

        foreach (var player in _players)
        {
            foreach (var hand in player.Hands)
            {
                if (!hand.IsFinished)
                    hand.Stand();
            }
        }

        DealerPlayInternal();
    }

    public void Timeout()
    {
        if (Phase == BjRoundPhase.Players)
            Stand();
    }

    // A dealer blackjack takes the whole bet of every hand, doubled and split hands too.
    public static (BjHandResult Result, long Payout) SettleHand(BlackjackHand hand, int dealerTotal, bool dealerBlackjack)
    {
        if (hand.IsBust)
            return (BjHandResult.Bust, 0);

        if (dealerBlackjack)
            return hand.IsNatural ? (BjHandResult.Push, hand.Bet) : (BjHandResult.Lose, 0);

        // 3:2 rounded down: the bet back, plus the bet, plus half the bet
        if (hand.IsNatural)
            return (BjHandResult.Blackjack, (hand.Bet * 2) + (hand.Bet / 2));

        var total = hand.Value.Total;
        if (dealerTotal > BlackjackHand.BLACKJACK || total > dealerTotal)
            return (BjHandResult.Win, hand.Bet * 2);

        return total == dealerTotal ? (BjHandResult.Push, hand.Bet) : (BjHandResult.Lose, 0);
    }

    public static List<BlackjackPayout> GetPayouts(BlackjackPlayer player)
    {
        var payouts = new List<BlackjackPayout>(player.Hands.Count * 2);
        for (var i = 0; i < player.Hands.Count; i++)
        {
            var hand = player.Hands[i];
            switch (hand.Result)
            {
                case BjHandResult.Blackjack:
                    payouts.Add(new(BlackjackTx.BLACKJACK, hand.Payout));
                    break;
                case BjHandResult.Win:
                    payouts.Add(new(BlackjackTx.WIN, hand.Payout));
                    break;
                case BjHandResult.Push:
                    payouts.Add(new(BlackjackTx.PUSH, hand.Payout));
                    break;
            }
        }

        return payouts;
    }

    private BlackjackHand RequireInternal(bool allowed)
        => allowed ? CurrentHand! : throw new InvalidOperationException("This action is not allowed now.");

    private void NextHandInternal()
    {
        while (PlayerIndex < _players.Length)
        {
            var hands = _players[PlayerIndex].Hands;
            while (HandIndex < hands.Count)
            {
                if (!hands[HandIndex].IsFinished)
                    return;

                HandIndex++;
            }

            PlayerIndex++;
            HandIndex = 0;
        }

        PlayerIndex = 0;
        DealerPlayInternal();
    }

    // The dealer always gets the second card, but hits only when a hand is left which the dealer's total decides.
    private void DealerPlayInternal()
    {
        Dealer.Add(_shoe.Draw());

        if (!Dealer.IsNatural && HasLiveHandInternal())
        {
            while (DealerMustHit(Dealer.Value))
                Dealer.Add(_shoe.Draw());
        }

        FinishInternal();
    }

    private static bool DealerMustHit(BjHandValue value)
        => value.Total < DEALER_STANDS_AT || (value.Total == DEALER_STANDS_AT && value.IsSoft);

    private bool HasLiveHandInternal()
    {
        foreach (var player in _players)
        {
            foreach (var hand in player.Hands)
            {
                if (!hand.IsBust && !hand.IsNatural)
                    return true;
            }
        }

        return false;
    }

    private void FinishInternal()
    {
        var dealerTotal = Dealer.Value.Total;
        var dealerBlackjack = DealerHasBlackjack;
        foreach (var player in _players)
        {
            foreach (var hand in player.Hands)
            {
                var (result, payout) = SettleHand(hand, dealerTotal, dealerBlackjack);
                hand.SetResult(result, payout);
            }
        }

        Phase = BjRoundPhase.Finished;
    }
}
