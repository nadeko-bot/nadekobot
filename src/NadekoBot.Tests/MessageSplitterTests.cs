using System.Collections.Generic;
using NadekoBot.Extensions;
using NUnit.Framework;

namespace NadekoBot.Tests;

public class MessageSplitterTests
{
    [Test]
    public void TextUnderLimit_ReturnsSingleChunkOrNone()
    {
        var results = new List<string>();
        MessageSplitter.Split("", 2000, results);
        Assert.That(results, Is.Empty);

        foreach (var text in new[] { "hello world", new string('a', 1900) })
        {
            results.Clear();
            MessageSplitter.Split(text, 2000, results);

            Assert.That(results, Has.Count.EqualTo(1), text);
            Assert.That(results[0], Is.EqualTo(text));
        }
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
    public void MultipleSplits_PreservesAllContentUnderMaxLength()
    {
        var words = new List<string>();
        for (var i = 0; i < 100; i++)
            words.Add($"word{i:D3}");

        var text = string.Join(" ", words);
        var results = new List<string>();
        MessageSplitter.Split(text, 200, results);

        Assert.That(results, Has.Count.GreaterThan(1));
        foreach (var chunk in results)
            Assert.That(chunk.Length, Is.LessThanOrEqualTo(200));

        Assert.That(string.Join(" ", results), Is.EqualTo(text));
    }
}
