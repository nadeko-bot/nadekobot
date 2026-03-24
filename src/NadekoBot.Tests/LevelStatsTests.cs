using NadekoBot.Db;
using NUnit.Framework;

namespace NadekoBot.Tests;

[TestFixture]
public class LevelStatsTests
{
    [Test]
    public void DefaultFormula_MatchesLegacyBehavior()
    {
        var stats = new LevelStats(1000);
        var statsParam = new LevelStats(1000, 9, 27);

        Assert.That(statsParam.Level, Is.EqualTo(stats.Level));
        Assert.That(statsParam.TotalXp, Is.EqualTo(stats.TotalXp));
        Assert.That(statsParam.LevelXp, Is.EqualTo(stats.LevelXp));
        Assert.That(statsParam.RequiredXp, Is.EqualTo(stats.RequiredXp));
    }

    [Test]
    public void CustomFormula_ChangesLevel()
    {
        var defaultStats = new LevelStats(500, 9, 27);
        var easyStats = new LevelStats(500, 1, 0);

        Assert.That(easyStats.Level, Is.GreaterThan(defaultStats.Level));
    }

    [Test]
    public void GetTotalXpReqForLevel_RoundTrips()
    {
        const int a = 15;
        const int c = 50;

        for (var level = 0; level < 100; level++)
        {
            var totalXp = LevelStats.GetTotalXpReqForLevel(level, a, c);
            var computedLevel = LevelStats.GetLevelByTotalXp(totalXp, a, c);
            Assert.That(computedLevel, Is.EqualTo(level),
                $"Round-trip failed for level {level}: totalXp={totalXp}, computedLevel={computedLevel}");
        }
    }

    [Test]
    public void CreateForLevel_WithCustomParams_ReturnsCorrectTotalXp()
    {
        const int a = 20;
        const int c = 100;

        var stats = LevelStats.CreateForLevel(10, a, c);

        Assert.That(stats.Level, Is.EqualTo(10));
        Assert.That(stats.LevelXp, Is.EqualTo(0));
    }

    [Test]
    public void SteepFormula_RequiresMoreXp()
    {
        const long level = 10;
        var defaultXp = LevelStats.GetTotalXpReqForLevel(level, 9, 27);
        var steepXp = LevelStats.GetTotalXpReqForLevel(level, 50, 100);

        Assert.That(steepXp, Is.GreaterThan(defaultXp));
    }
}
