using Nadeko.Econ;

namespace NadekoBot.Modules.Gambling.Betdraw;

public sealed class BetdrawGame
{
    private static readonly NadekoRandom _rng = new();
    private readonly RegularDeck _deck;

    private const decimal SINGLE_GUESS_MULTI = 2.075M;
    private const decimal DOUBLE_GUESS_MULTI = 4.15M;
    
    public BetdrawGame()
    {
        _deck = new RegularDeck();
    }
    
    public BetdrawResult Draw(BetdrawValueGuess? val, BetdrawColorGuess? col, decimal amount)
    {
        if (val is null && col is null)
            throw new ArgumentNullException(nameof(val));

        var card = _deck.Peek(_rng.Next(0, 52))!;

        var realVal = (int)card.Value < 7
            ? BetdrawValueGuess.Low
            : BetdrawValueGuess.High;

        var realCol = card.Suit is RegularSuit.Diamonds or RegularSuit.Hearts
            ? BetdrawColorGuess.Red
            : BetdrawColorGuess.Black;
        
        // if card is 7, autoloss
        if (card.Value == RegularValue.Seven)
        {
            return new()
            {
                Won = 0M,
                Multiplier = 0M,
                ResultType = BetdrawResultType.Lose,
                Card = card,
            };
        }

        byte win = 0;
        if (val is BetdrawValueGuess valGuess)
        {
            if (realVal != valGuess)
                return new()
                {
                    Won = 0M,
                    Multiplier = 0M,
                    ResultType = BetdrawResultType.Lose,
                    Card = card
                };

            ++win;
        }
        
        if (col is BetdrawColorGuess colGuess)
        {
            if (realCol != colGuess)
                return new()
                {
                    Won = 0M,
                    Multiplier = 0M,
                    ResultType = BetdrawResultType.Lose,
                    Card = card
                };

            ++win;
        }

        var multi = win == 1
            ? SINGLE_GUESS_MULTI
            : DOUBLE_GUESS_MULTI;

        return new()
        {
            Won = amount * multi,
            Multiplier = multi,
            ResultType = BetdrawResultType.Win,
            Card = card
        };
    }
}