#nullable disable
namespace Nadeko.Econ;

public class Deck
{
    public enum CardSuit
    {
        Spades = 1,
        Hearts = 2,
        Diamonds = 3,
        Clubs = 4
    }

    private static readonly Dictionary<int, string> _cardNames = new()
    {
        { 1, "Ace" },
        { 2, "Two" },
        { 3, "Three" },
        { 4, "Four" },
        { 5, "Five" },
        { 6, "Six" },
        { 7, "Seven" },
        { 8, "Eight" },
        { 9, "Nine" },
        { 10, "Ten" },
        { 11, "Jack" },
        { 12, "Queen" },
        { 13, "King" }
    };

    private static Dictionary<string, Func<List<Card>, bool>> handValues;

    public List<Card> CardPool { get; set; }
    private readonly Random _r = new NadekoRandom();

    static Deck()
        => InitHandValues();

    /// <summary>
    ///     Creates a new instance of the BlackJackGame, this allows you to create multiple games running at one time.
    /// </summary>
    public Deck()
        => RefillPool();

    /// <summary>
    ///     Restart the game of blackjack. It will only refill the pool for now. Probably wont be used, unless you want to have
    ///     only 1 bjg running at one time,
    ///     then you will restart the same game every time.
    /// </summary>
    public void Restart()
        => RefillPool();

    /// <summary>
    ///     Removes all cards from the pool and refills the pool with all of the possible cards. NOTE: I think this is too
    ///     expensive.
    ///     We should probably make it so it copies another premade list with all the cards, or something.
    /// </summary>
    protected virtual void RefillPool()
    {
        CardPool = new(52);
        //foreach suit
        for (var j = 1; j < 14; j++)
            // and number
        for (var i = 1; i < 5; i++)
            //generate a card of that suit and number and add it to the pool

            // the pool will go from ace of spades,hears,diamonds,clubs all the way to the king of spades. hearts, ...
            CardPool.Add(new((CardSuit)i, j));
    }

    /// <summary>
    ///     Take a card from the pool, you either take it from the top if the deck is shuffled, or from a random place if the
    ///     deck is in the default order.
    /// </summary>
    /// <returns>A card from the pool</returns>
    public Card Draw()
    {
        if (CardPool.Count == 0)
            Restart();
        //you can either do this if your deck is not shuffled

        var num = _r.Next(0, CardPool.Count);
        var c = CardPool[num];
        CardPool.RemoveAt(num);
        return c;

        // if you want to shuffle when you fill, then take the first one
        /*
        Card c = cardPool[0];
        cardPool.RemoveAt(0);
        return c;
        */
    }

    /// <summary>
    ///     Shuffles the deck. Use this if you want to take cards from the top of the deck, instead of randomly. See DrawACard
    ///     method.
    /// </summary>
    private void Shuffle()
    {
        if (CardPool.Count <= 1)
            return;
        var orderedPool = CardPool.Shuffle();
        CardPool ??= orderedPool.ToList();
    }

    public override string ToString()
        => string.Concat(CardPool.Select(c => c.ToString())) + Environment.NewLine;

    private static void InitHandValues()
    {
        bool HasPair(List<Card> cards)
        {
            return cards.GroupBy(card => card.Number).Count(group => group.Count() == 2) == 1;
        }

        bool IsPair(List<Card> cards)
        {
            return cards.GroupBy(card => card.Number).Count(group => group.Count() == 3) == 0 && HasPair(cards);
        }

        bool IsTwoPair(List<Card> cards)
        {
            return cards.GroupBy(card => card.Number).Count(group => group.Count() == 2) == 2;
        }

        bool IsStraight(List<Card> cards)
        {
            if (cards.GroupBy(card => card.Number).Count() != cards.Count())
                return false;
            var toReturn = cards.Max(card => card.Number) - cards.Min(card => card.Number) == 4;
            if (toReturn || cards.All(c => c.Number != 1))
                return toReturn;

            var newCards = cards.Select(c => c.Number == 1 ? new(c.Suit, 14) : c).ToArray();
            return newCards.Max(card => card.Number) - newCards.Min(card => card.Number) == 4;
        }

        bool HasThreeOfKind(List<Card> cards)
        {
            return cards.GroupBy(card => card.Number).Any(group => group.Count() == 3);
        }

        bool IsThreeOfKind(List<Card> cards)
        {
            return HasThreeOfKind(cards) && !HasPair(cards);
        }

        bool IsFlush(List<Card> cards)
        {
            return cards.GroupBy(card => card.Suit).Count() == 1;
        }

        bool IsFourOfKind(List<Card> cards)
        {
            return cards.GroupBy(card => card.Number).Any(group => group.Count() == 4);
        }

        bool IsFullHouse(List<Card> cards)
        {
            return HasPair(cards) && HasThreeOfKind(cards);
        }

        bool HasStraightFlush(List<Card> cards)
        {
            return IsFlush(cards) && IsStraight(cards);
        }

        bool IsRoyalFlush(List<Card> cards)
        {
            return cards.Min(card => card.Number) == 1
                   && cards.Max(card => card.Number) == 13
                   && HasStraightFlush(cards);
        }

        bool IsStraightFlush(List<Card> cards)
        {
            return HasStraightFlush(cards) && !IsRoyalFlush(cards);
        }

        handValues = new()
        {
            { "Royal Flush", IsRoyalFlush },
            { "Straight Flush", IsStraightFlush },
            { "Four Of A Kind", IsFourOfKind },
            { "Full House", IsFullHouse },
            { "Flush", IsFlush },
            { "Straight", IsStraight },
            { "Three Of A Kind", IsThreeOfKind },
            { "Two Pairs", IsTwoPair },
            { "A Pair", IsPair }
        };
    }

    public static string GetHandValue(List<Card> cards)
    {
        if (handValues is null)
            InitHandValues();

        foreach (var kvp in handValues.Where(x => x.Value(cards)))
            return kvp.Key;
        return "High card " + (cards.FirstOrDefault(c => c.Number == 1)?.GetValueText() ?? cards.Max().GetValueText());
    }

    public class Card : IComparable
    {
        private static readonly IReadOnlyDictionary<CardSuit, string> _suitToSuitChar = new Dictionary<CardSuit, string>
        {
            { CardSuit.Diamonds, "♦" },
            { CardSuit.Clubs, "♣" },
            { CardSuit.Spades, "♠" },
            { CardSuit.Hearts, "♥" }
        };

        private static readonly IReadOnlyDictionary<string, CardSuit> _suitCharToSuit = new Dictionary<string, CardSuit>
        {
            { "♦", CardSuit.Diamonds },
            { "d", CardSuit.Diamonds },
            { "♣", CardSuit.Clubs },
            { "c", CardSuit.Clubs },
            { "♠", CardSuit.Spades },
            { "s", CardSuit.Spades },
            { "♥", CardSuit.Hearts },
            { "h", CardSuit.Hearts }
        };

        private static readonly IReadOnlyDictionary<char, int> _numberCharToNumber = new Dictionary<char, int>
        {
            { 'a', 1 },
            { '2', 2 },
            { '3', 3 },
            { '4', 4 },
            { '5', 5 },
            { '6', 6 },
            { '7', 7 },
            { '8', 8 },
            { '9', 9 },
            { 't', 10 },
            { 'j', 11 },
            { 'q', 12 },
            { 'k', 13 }
        };

        public CardSuit Suit { get; }
        public int Number { get; }

        public string FullName
        {
            get
            {
                var str = string.Empty;

                if (Number is <= 10 and > 1)
                    str += "_" + Number;
                else
                    str += GetValueText().ToLowerInvariant();
                return str + "_of_" + Suit.ToString().ToLowerInvariant();
            }
        }

        private readonly string[] _regIndicators =
        [
            "🇦", ":two:", ":three:", ":four:", ":five:", ":six:", ":seven:", ":eight:", ":nine:", ":keycap_ten:",
            "🇯", "🇶", "🇰"
        ];

        public Card(CardSuit s, int cardNum)
        {
            Suit = s;
            Number = cardNum;
        }

        public string GetValueText()
            => _cardNames[Number];
        
        public override string ToString()
            => _cardNames[Number] + " Of " + Suit;

        public int CompareTo(object obj)
        {
            if (obj is not Card card)
                return 0;
            return Number - card.Number;
        }

        public static Card Parse(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                throw new ArgumentNullException(nameof(input));

            if (input.Length != 2
                || !_numberCharToNumber.TryGetValue(input[0], out var n)
                || !_suitCharToSuit.TryGetValue(input[1].ToString(), out var s))
                throw new ArgumentException("Invalid input", nameof(input));

            return new(s, n);
        }

        public string GetEmojiString()
        {
            var str = string.Empty;

            str += _regIndicators[Number - 1];
            str += _suitToSuitChar[Suit];

            return str;
        }
    }
}