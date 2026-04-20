using NadekoBot.Modules.Administration;
using NUnit.Framework;

namespace NadekoBot.Tests;

[TestFixture]
public sealed class AntiRaidStatsTests
{
    [Test]
    public void IncrementDecrement_TracksCorrectly()
    {
        var stats = new AntiRaidStats();

        Assert.That(stats.IncrementUsers(), Is.EqualTo(1));
        Assert.That(stats.IncrementUsers(), Is.EqualTo(2));
        Assert.That(stats.DecrementUsers(), Is.EqualTo(1));
        Assert.That(stats.UsersCount, Is.EqualTo(1));
    }

    [Test]
    public void ResetUsers_SetsCountToZero()
    {
        var stats = new AntiRaidStats();
        stats.IncrementUsers();
        stats.IncrementUsers();
        stats.IncrementUsers();

        stats.ResetUsers();

        Assert.That(stats.UsersCount, Is.EqualTo(0));
    }

    [Test]
    public void ResetUsers_FromNegativeValue_SetsToZero()
    {
        var stats = new AntiRaidStats();
        stats.DecrementUsers();
        stats.DecrementUsers();
        Assert.That(stats.UsersCount, Is.EqualTo(-2));

        stats.ResetUsers();

        Assert.That(stats.UsersCount, Is.EqualTo(0));
    }
}
