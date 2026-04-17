using System.Collections.Generic;
using NadekoBot.Extensions;
using NUnit.Framework;

namespace NadekoBot.Tests;

public class MessageSplitterTests
{
    [Test]
    public void ShortText_ReturnsSingleChunk()
    {
        var results = new List<string>();
        MessageSplitter.Split("hello world", 2000, results);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo("hello world"));
    }

    [Test]
    public void EmptyText_ReturnsEmpty()
    {
        var results = new List<string>();
        MessageSplitter.Split("", 2000, results);

        Assert.That(results, Is.Empty);
    }

    [Test]
    public void UnderBufferedLimit_ReturnsSingleChunk()
    {
        var text = new string('a', 1900);
        var results = new List<string>();
        MessageSplitter.Split(text, 2000, results);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo(text));
    }

    [Test]
    public void SplitsAtWordBoundary()
    {
        var first = new string('a', 95);
        var second = new string('b', 50);
        var text = $"{first} {second}";
        var results = new List<string>();
        MessageSplitter.Split(text, 200, results);

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo(first));
        Assert.That(results[1], Is.EqualTo(second));
    }

    [Test]
    public void NoWhitespace_HardBreaksAtLimit()
    {
        var text = new string('x', 300);
        var results = new List<string>();
        MessageSplitter.Split(text, 200, results);

        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo(new string('x', 100)));
        Assert.That(results[1], Is.EqualTo(new string('x', 100)));
        Assert.That(results[2], Is.EqualTo(new string('x', 100)));
    }

    [Test]
    public void MultipleSplits_PreservesAllContent()
    {
        var words = new List<string>();
        for (var i = 0; i < 100; i++)
            words.Add($"word{i:D3}");

        var text = string.Join(" ", words);
        var results = new List<string>();
        MessageSplitter.Split(text, 200, results);

        var reconstructed = string.Join(" ", results);
        Assert.That(reconstructed, Is.EqualTo(text));
    }

    [Test]
    public void SplitsAtNewline()
    {
        var first = new string('a', 90);
        var second = new string('b', 50);
        var text = $"{first}\n{second}";
        var results = new List<string>();
        MessageSplitter.Split(text, 200, results);

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo(first));
    }

    [Test]
    public void AllChunksUnderMaxLength()
    {
        var words = new List<string>();
        for (var i = 0; i < 200; i++)
            words.Add($"longword{i:D4}");

        var text = string.Join(" ", words);
        var results = new List<string>();
        MessageSplitter.Split(text, 2000, results);

        foreach (var chunk in results)
            Assert.That(chunk.Length, Is.LessThanOrEqualTo(2000));
    }
}
