#nullable disable
using Nadeko.Econ;

namespace NadekoBot.Modules.Gambling.Common.Blackjack;

public abstract class Player
{
    public List<Deck.Card> Cards { get; } = new();

    public int GetHandValue()
    {
        var val = GetRawHandValue();

        // while the hand value is greater than 21, for each ace you have in the deck
        // reduce the value by 10 until it drops below 22
        // (emulating the fact that ace is either a 1 or a 11)
        var i = Cards.Count(x => x.Number == 1);
        while (val > 21 && i-- > 0)
            val -= 10;
        return val;
    }

    public int GetRawHandValue()
        => Cards.Sum(x => x.Number == 1 ? 11 : x.Number >= 10 ? 10 : x.Number);
}

public class Dealer : Player
{
}

public class User : Player
{
    public enum UserState
    {
        Waiting,
        Stand,
        Bust,
        Blackjack,
        Won,
        Lost
    }

    public UserState State { get; set; } = UserState.Waiting;
    public long Bet { get; set; }
    public IUser DiscordUser { get; }

    public bool Done
        => State != UserState.Waiting;

    public User(IUser user, long bet)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bet);

        Bet = bet;
        DiscordUser = user;
    }
}