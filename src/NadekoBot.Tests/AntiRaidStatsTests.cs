using NadekoBot.Modules.Administration;
using NUnit.Framework;

namespace NadekoBot.Tests;

[TestFixture]
public sealed class AntiRaidStatsTests
{
    [Test]
    public void UserCount_TracksJoinsAndResetsToZero()
    {
        var stats = new AntiRaidStats();

        Assert.That(stats.IncrementUsers(), Is.EqualTo(1));
        Assert.That(stats.IncrementUsers(), Is.EqualTo(2));
        Assert.That(stats.DecrementUsers(), Is.EqualTo(1));
        stats.ResetUsers();
        Assert.That(stats.UsersCount, Is.EqualTo(0));

        stats.DecrementUsers();
        stats.DecrementUsers();
        Assert.That(stats.UsersCount, Is.EqualTo(-2));
        stats.ResetUsers();
        Assert.That(stats.UsersCount, Is.EqualTo(0));
    }
}
