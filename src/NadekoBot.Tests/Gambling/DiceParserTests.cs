using NadekoBot.Modules.Gambling;
using NUnit.Framework;

namespace NadekoBot.Tests.Gambling;

public class DiceParserTests
{
    [Test]
    public void TryParseRange_AcceptsNumbersAndRangesAndRejectsGarbage()
    {
        Assert.That(DiceParser.TryParseRange("100", out var min, out var max), Is.True);
        Assert.That((min, max), Is.EqualTo((0L, 100L)));

        Assert.That(DiceParser.TryParseRange(" 5-15 ", out min, out max), Is.True);
        Assert.That((min, max), Is.EqualTo((5L, 15L)));

        Assert.That(DiceParser.TryParseRange("-10--2", out min, out max), Is.True);
        Assert.That((min, max), Is.EqualTo((-10L, -2L)));

        Assert.That(DiceParser.TryParseRange("2147483647", out min, out max), Is.True);
        Assert.That(max, Is.EqualTo((long)int.MaxValue));

        foreach (var bad in new[] { "", "abc", "5-", "-", "1-2-3", "99999999999" })
            Assert.That(DiceParser.TryParseRange(bad, out _, out _), Is.False, bad);
    }

    [Test]
    public void TryParseDnd_ParsesCountSidesModifiersAndFate()
    {
        Assert.That(DiceParser.TryParseDnd("3d6", out var roll), Is.True);
        Assert.That(roll, Is.EqualTo(new DndRoll(3, 6, 0, 0)));

        Assert.That(DiceParser.TryParseDnd("D20+3", out roll), Is.True);
        Assert.That(roll, Is.EqualTo(new DndRoll(1, 20, 3, 0)));

        Assert.That(DiceParser.TryParseDnd("2d10-1+4-2", out roll), Is.True);
        Assert.That(roll, Is.EqualTo(new DndRoll(2, 10, 4, 3)));

        Assert.That(DiceParser.TryParseDnd("2147483648d6", out roll), Is.True);
        Assert.That(roll.Count, Is.EqualTo(int.MaxValue));

        Assert.That(DiceParser.TryParseDnd("4dF", out roll), Is.True);
        Assert.That(roll.IsFate, Is.True);
        Assert.That(roll.Count, Is.EqualTo(4));

        foreach (var bad in new[] { "abc", "2d", "2d0", "xd6", "2d6+", "2d6*2", "-1d6", "2dF+1" })
            Assert.That(DiceParser.TryParseDnd(bad, out _), Is.False, bad);
    }
}
