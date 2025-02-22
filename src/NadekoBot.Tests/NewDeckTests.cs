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
    public void TestCount()
    {
        Assert.That(52, Is.EqualTo(_deck.TotalCount));
        Assert.That(52, Is.EqualTo(_deck.CurrentCount));
    }
    
    [Test]
    public void TestDeckDraw()
    {
        var card = _deck.Draw();
        
        Assert.That(card, Is.Not.Null);
        Assert.That(card.Suit, Is.EqualTo(RegularSuit.Hearts));
        Assert.That(card.Value, Is.EqualTo(RegularValue.Ace));
        Assert.That(_deck.CurrentCount, Is.EqualTo(_deck.TotalCount - 1));
    }

    [Test]
    public void TestDeckSpent()
    {
        for (var i = 0; i < _deck.TotalCount - 1; ++i)
        {
            _deck.Draw();
        }

        var lastCard = _deck.Draw();
        
        Assert.That(lastCard, Is.Not.Null);
        Assert.That(lastCard, Is.EqualTo(new RegularCard(RegularSuit.Spades, RegularValue.King)));

        var noCard = _deck.Draw();
        
        Assert.That(noCard, Is.Null);
    }

    [Test]
    public void TestCardGetName()
    {
        var ace = _deck.Draw()!;
        var two = _deck.Draw()!;
        
        Assert.That("Ace of Hearts", Is.EqualTo(ace.GetName()));
        Assert.That("Two of Hearts", Is.EqualTo(two.GetName()));
    }

    [Test]
    public void TestPeek()
    {
        var ace = _deck.Peek()!;

        var tenOfSpades = _deck.Peek(48);
        Assert.That(new RegularCard(RegularSuit.Hearts, RegularValue.Ace), Is.EqualTo(ace));
        Assert.That(new RegularCard(RegularSuit.Spades, RegularValue.Ten), Is.EqualTo(tenOfSpades));
    }

    [Test]
    public void TestMultipleDeck()
    {
        var quadDeck = new MultipleRegularDeck(4);
        var count = quadDeck.TotalCount;
        
        Assert.That(52 * 4, Is.EqualTo(count));

        var card = quadDeck.Peek(54);
        Assert.That(new RegularCard(RegularSuit.Hearts, RegularValue.Three), Is.EqualTo(card));
    }
}