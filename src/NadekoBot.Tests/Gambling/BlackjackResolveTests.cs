using Discord;
using NSubstitute;
using Nadeko.Econ;
using NadekoBot.Modules.Gambling.Common.Blackjack;
using NUnit.Framework;
using BjUser = NadekoBot.Modules.Gambling.Common.Blackjack.User;

namespace NadekoBot.Tests.Gambling;

public class BlackjackResolveTests
{
    private static Dealer Dealer(params int[] cards)
    {
        var d = new Dealer();
        foreach (var c in cards)
            d.Cards.Add(new Deck.Card(Deck.CardSuit.Hearts, c));
        return d;
    }

    private static BjUser Player(BjUser.UserState state, params int[] cards)
    {
        var u = new BjUser(Substitute.For<IUser>(), 100) { State = state };
        foreach (var c in cards)
            u.Cards.Add(new Deck.Card(Deck.CardSuit.Spades, c));
        return u;
    }

    [Test]
    public void Resolve_EqualValuesPushAndHigherValueWins()
    {
        var dealer18 = Dealer(10, 8);

        Assert.That(BlackjackResolve(Player(BjUser.UserState.Stand, 10, 8), dealer18), Is.EqualTo(BjUser.UserState.Push));
        Assert.That(BlackjackResolve(Player(BjUser.UserState.Stand, 10, 9), dealer18), Is.EqualTo(BjUser.UserState.Won));
        Assert.That(BlackjackResolve(Player(BjUser.UserState.Stand, 10, 7), dealer18), Is.EqualTo(BjUser.UserState.Lost));
        Assert.That(BlackjackResolve(Player(BjUser.UserState.Bust, 10, 8, 5), Dealer(10, 6, 9)), Is.EqualTo(BjUser.UserState.Lost));
        Assert.That(BlackjackResolve(Player(BjUser.UserState.Stand, 10, 2), Dealer(10, 6, 9)), Is.EqualTo(BjUser.UserState.Won));
    }

    [Test]
    public void Resolve_OnlyANaturalBeatsADealer21()
    {
        var dealer21 = Dealer(10, 5, 6);
        var dealerNatural = Dealer(1, 13);

        Assert.That(BlackjackResolve(Player(BjUser.UserState.Stand, 10, 5, 6), dealer21), Is.EqualTo(BjUser.UserState.Push));
        Assert.That(BlackjackResolve(Player(BjUser.UserState.Blackjack, 1, 12), dealer21), Is.EqualTo(BjUser.UserState.Won));
        Assert.That(BlackjackResolve(Player(BjUser.UserState.Blackjack, 1, 12), dealerNatural), Is.EqualTo(BjUser.UserState.Push));
        Assert.That(BlackjackResolve(Player(BjUser.UserState.Stand, 10, 5, 6), dealerNatural), Is.EqualTo(BjUser.UserState.Lost));
    }

    [Test]
    public void Settle_OnlyANaturalPaysThreeToTwo()
    {
        Assert.That(Payout(Player(BjUser.UserState.Blackjack, 1, 12), Dealer(10, 5, 6)), Is.EqualTo(250));
        Assert.That(Payout(Player(BjUser.UserState.Blackjack, 1, 12), Dealer(10, 6, 9)), Is.EqualTo(250));
        Assert.That(Payout(Player(BjUser.UserState.Blackjack, 1, 12), Dealer(1, 13)), Is.EqualTo(100));
        Assert.That(Payout(Player(BjUser.UserState.Stand, 10, 5, 6), Dealer(10, 8)), Is.EqualTo(200));
        Assert.That(Payout(Player(BjUser.UserState.Stand, 10, 8), Dealer(10, 8)), Is.EqualTo(100));
        Assert.That(Payout(Player(BjUser.UserState.Bust, 10, 8, 5), Dealer(10, 6, 9)), Is.EqualTo(0));

        // the half of an odd bet is rounded down
        Assert.That(Blackjack.GetPayout(BjUser.UserState.Won, true, 5), Is.EqualTo(12));
    }

    private static long Payout(BjUser u, Dealer d)
        => Blackjack.Settle(u, d).Payout;

    private static BjUser.UserState BlackjackResolve(BjUser u, Dealer d)
        => Blackjack.Resolve(u, d);
}
