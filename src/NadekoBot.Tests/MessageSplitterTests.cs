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
    public void LongText_SplitsAtNewlinesAndWordsElseHardBreaks()
    {
        var results = new List<string>();

        var first = new string('a', 95);
        var second = new string('b', 50);
        MessageSplitter.Split($"{first} {second}", 200, results);
        Assert.That(results, Is.EqualTo(new[] { first, second }));

        results.Clear();
        var line = new string('a', 90);
        MessageSplitter.Split($"{line}\n{second}", 200, results);
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0], Is.EqualTo(line));

        results.Clear();
        MessageSplitter.Split(new string('x', 300), 200, results);
        Assert.That(results, Is.EqualTo(new[] { new string('x', 100), new string('x', 100), new string('x', 100) }));

        var words = new List<string>();
        for (var i = 0; i < 100; i++)
            words.Add($"word{i:D3}");

        var text = string.Join(" ", words);
        results.Clear();
        MessageSplitter.Split(text, 200, results);

        Assert.That(results, Has.Count.GreaterThan(1));
        foreach (var chunk in results)
            Assert.That(chunk.Length, Is.LessThanOrEqualTo(200));

        Assert.That(string.Join(" ", results), Is.EqualTo(text));
    }
}
