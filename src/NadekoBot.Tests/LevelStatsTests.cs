using NadekoBot.Db;
using NUnit.Framework;

namespace NadekoBot.Tests;

[TestFixture]
public class LevelStatsTests
{
    [Test]
    public void Formula_RoundTripsAndChangesWithItsParameters()
    {
        var legacy = new LevelStats(1000);
        var withDefaults = new LevelStats(1000, 9, 27);
        Assert.That(withDefaults.Level, Is.EqualTo(legacy.Level));
        Assert.That(withDefaults.TotalXp, Is.EqualTo(legacy.TotalXp));
        Assert.That(withDefaults.LevelXp, Is.EqualTo(legacy.LevelXp));
        Assert.That(withDefaults.RequiredXp, Is.EqualTo(legacy.RequiredXp));

        Assert.That(new LevelStats(500, 1, 0).Level, Is.GreaterThan(new LevelStats(500, 9, 27).Level));
        Assert.That(LevelStats.GetTotalXpReqForLevel(10, 50, 100),
            Is.GreaterThan(LevelStats.GetTotalXpReqForLevel(10, 9, 27)));

        for (var level = 0; level < 100; level++)
        {
            var totalXp = LevelStats.GetTotalXpReqForLevel(level, 15, 50);
            Assert.That(LevelStats.GetLevelByTotalXp(totalXp, 15, 50), Is.EqualTo(level), $"level {level}");
        }

        var created = LevelStats.CreateForLevel(10, 20, 100);
        Assert.That(created.Level, Is.EqualTo(10));
        Assert.That(created.LevelXp, Is.EqualTo(0));
    }
}