using System.Buffers;
using System.Buffers.Binary;

namespace NadekoBot.Modules.Gambling.Blackjack;

// The cards of a round which is not settled yet, so a restart can finish the round instead of refunding it.
public static class BlackjackRoundStateCodec
{
    private const byte VERSION = 1;

    private const byte DOUBLED = 1 << 0;
    private const byte FROM_SPLIT = 1 << 1;
    private const byte STOOD = 1 << 2;

    private const int MAX_PLAYERS = BlackjackTable.MAX_SEATS;
    private const int MAX_HANDS_PER_PLAYER = BlackjackRound.MAX_HANDS;
    private const int MAX_HAND_CARDS = BlackjackHand.BLACKJACK;
    private const int MAX_CARD_BYTES = 2;

    // version, phase and two indexes, the dealer, five players with two hands each, and the whole shoe
    private const int MAX_BYTES = 4
                                  + 1 + (MAX_HAND_CARDS * MAX_CARD_BYTES)
                                  + 1 + MAX_PLAYERS * (sizeof(ulong) + 1)
                                  + MAX_PLAYERS * MAX_HANDS_PER_PLAYER
                                  * (sizeof(long) + 1 + 1 + (MAX_HAND_CARDS * MAX_CARD_BYTES))
                                  + sizeof(ushort) + BlackjackShoe.SIZE * MAX_CARD_BYTES;

    public static byte[] Write(BlackjackRound round)
    {
        if (round.Players.Length > MAX_PLAYERS)
            throw new InvalidOperationException("A saved round holds at most five players.");

        var buffer = ArrayPool<byte>.Shared.Rent(MAX_BYTES);
        try
        {
            var span = buffer.AsSpan();
            var pos = 0;
            span[pos++] = VERSION;
            span[pos++] = (byte)round.Phase;
            span[pos++] = (byte)round.PlayerIndex;
            span[pos++] = (byte)round.HandIndex;
            pos = WriteCardsInternal(span, pos, round.Dealer.Cards);

            span[pos++] = (byte)round.Players.Length;
            foreach (var player in round.Players)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(span[pos..], player.UserId);
                pos += sizeof(ulong);
                span[pos++] = (byte)player.Hands.Count;

                foreach (var hand in player.Hands)
                {
                    BinaryPrimitives.WriteInt64LittleEndian(span[pos..], hand.BaseBet);
                    pos += sizeof(long);
                    span[pos++] = (byte)((hand.IsDoubled ? DOUBLED : 0)
                                         | (hand.IsFromSplit ? FROM_SPLIT : 0)
                                         | (hand.IsStood ? STOOD : 0));
                    pos = WriteCardsInternal(span, pos, hand.Cards);
                }
            }

            var shoe = round.ShoeRemaining;
            pos = WriteShoeInternal(span, pos, shoe);

            return span[..pos].ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // Null when the saved round cannot be read, so the caller refunds it instead of settling it.
    public static BlackjackRound? Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 || data[0] != VERSION)
            return null;

        var pos = 1;
        var phase = (BjRoundPhase)data[pos++];
        if (phase is not (BjRoundPhase.Players or BjRoundPhase.Finished))
            return null;

        var playerIndex = data[pos++];
        var handIndex = data[pos++];

        Span<BjCard> cards = stackalloc BjCard[MAX_HAND_CARDS];
        if (!TryReadCardsInternal(data, ref pos, cards, out var dealerCount))
            return null;

        var dealer = new BjCard[dealerCount];
        cards[..dealerCount].CopyTo(dealer);

        if (dealerCount == 0 || pos >= data.Length || data[pos] is 0 or > MAX_PLAYERS)
            return null;

        var players = new BlackjackPlayer[data[pos++]];
        for (var p = 0; p < players.Length; p++)
        {
            if (data.Length - pos < sizeof(ulong) + 1)
                return null;

            var userId = BinaryPrimitives.ReadUInt64LittleEndian(data[pos..]);
            pos += sizeof(ulong);

            var handCount = data[pos++];
            if (handCount is 0 or > MAX_HANDS_PER_PLAYER)
                return null;

            var hands = new BlackjackHand[handCount];
            for (var h = 0; h < hands.Length; h++)
            {
                if (data.Length - pos < sizeof(long) + 1)
                    return null;

                var baseBet = BinaryPrimitives.ReadInt64LittleEndian(data[pos..]);
                pos += sizeof(long);

                var flags = data[pos++];
                if (!TryReadCardsInternal(data, ref pos, cards, out var cardCount) || cardCount == 0)
                    return null;

                hands[h] = BlackjackHand.Restore(cards[..cardCount],
                    baseBet,
                    (flags & DOUBLED) != 0,
                    (flags & FROM_SPLIT) != 0,
                    (flags & STOOD) != 0);
            }

            var player = new BlackjackPlayer(userId, hands[0].BaseBet);
            player.Hands.Clear();
            foreach (var hand in hands)
                player.Hands.Add(hand);

            players[p] = player;
        }

        if (data.Length - pos < sizeof(ushort))
            return null;

        Span<BjCard> shoeCards = stackalloc BjCard[BlackjackShoe.SIZE];
        if (!TryReadShoeInternal(data, ref pos, shoeCards, out var shoeCount))
            return null;

        var shoe = BlackjackShoe.FromOrder(shoeCards[..shoeCount]);
        return BlackjackRound.Restore(shoe, players, dealer, phase, playerIndex, handIndex);
    }

    private static int WriteCardsInternal(Span<byte> dest, int pos, ReadOnlySpan<BjCard> cards)
    {
        dest[pos++] = (byte)cards.Length;
        foreach (var card in cards)
        {
            dest[pos++] = card.Rank;
            dest[pos++] = card.Suit;
        }

        return pos;
    }

    private static bool TryReadCardsInternal(
        ReadOnlySpan<byte> data,
        ref int pos,
        Span<BjCard> dest,
        out int count)
    {
        count = 0;
        if (pos >= data.Length)
            return false;

        return TryReadCardPairsInternal(data, ref pos, dest, data[pos++], out count);
    }

    // The shoe holds more cards than a byte counts, so its length is a ushort and the cards follow it.
    private static int WriteShoeInternal(Span<byte> dest, int pos, ReadOnlySpan<BjCard> cards)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(dest[pos..], (ushort)cards.Length);
        pos += sizeof(ushort);

        foreach (var card in cards)
        {
            dest[pos++] = card.Rank;
            dest[pos++] = card.Suit;
        }

        return pos;
    }

    private static bool TryReadShoeInternal(ReadOnlySpan<byte> data, ref int pos, Span<BjCard> dest, out int count)
    {
        count = 0;
        if (data.Length - pos < sizeof(ushort))
            return false;

        var stored = BinaryPrimitives.ReadUInt16LittleEndian(data[pos..]);
        pos += sizeof(ushort);

        return TryReadCardPairsInternal(data, ref pos, dest, stored, out count);
    }

    private static bool TryReadCardPairsInternal(
        ReadOnlySpan<byte> data,
        ref int pos,
        Span<BjCard> dest,
        int stored,
        out int count)
    {
        count = 0;
        if (stored > dest.Length || data.Length - pos < stored * MAX_CARD_BYTES)
            return false;

        for (var i = 0; i < stored; i++)
        {
            var rank = data[pos++];
            var suit = data[pos++];
            if (rank is 0 or > BjCard.KING || suit >= BjCard.SUITS)
                return false;

            dest[i] = new(rank, suit);
        }

        count = stored;
        return true;
    }
}
