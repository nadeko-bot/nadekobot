using Nadeko.Econ;
using NUnit.Framework;

namespace NadekoBot.Tests;

public class NewDeckTests
{
    private RegularDeck _deck;

    [SetUp]
    public void Setup()
    {
        _deck = new RegularDeck();
    }

    [Test]
    public void DeckDrawsInOrderUntilEmpty()
    {
        Assert.That(_deck.TotalCount, Is.EqualTo(52));
        Assert.That(_deck.Peek(), Is.EqualTo(new RegularCard(RegularSuit.Hearts, RegularValue.Ace)));
        Assert.That(_deck.Peek(48), Is.EqualTo(new RegularCard(RegularSuit.Spades, RegularValue.Ten)));

        Assert.That(_deck.Draw()!.GetName(), Is.EqualTo("Ace of Hearts"));
        Assert.That(_deck.CurrentCount, Is.EqualTo(_deck.TotalCount - 1));
        Assert.That(_deck.Draw()!.GetName(), Is.EqualTo("Two of Hearts"));

        while (_deck.CurrentCount > 1)
            _deck.Draw();

        Assert.That(_deck.Draw(), Is.EqualTo(new RegularCard(RegularSuit.Spades, RegularValue.King)));
        Assert.That(_deck.Draw(), Is.Null);

        var quadDeck = new MultipleRegularDeck(4);
        Assert.That(quadDeck.TotalCount, Is.EqualTo(52 * 4));
        Assert.That(quadDeck.Peek(54), Is.EqualTo(new RegularCard(RegularSuit.Hearts, RegularValue.Three)));
    }
}