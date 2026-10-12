#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Xp.Services;
using NadekoBot.Tests.Waifu;
using NUnit.Framework;

namespace NadekoBot.Tests.Xp;

public sealed class XpRoleRewardTests
{
    private const ulong GUILD_ID = 1;

    private TestDbService _db = null!;
    private XpService _svc = null!;

    [SetUp]
    public void SetUp()
    {
        _db = new TestDbService();
        _svc = new XpService(null!, _db, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!);
    }

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    [Test]
    public void LevelUp_ChangesEachRoleOnceAndTheHighestPassedLevelWins()
    {
        XpRoleReward[] rewards =
        [
            new() { Level = 3, RoleId = 4, Remove = false },
            new() { Level = 5, RoleId = 1, Remove = false },
            new() { Level = 10, RoleId = 1, Remove = true },
            new() { Level = 7, RoleId = 2, Remove = false },
            new() { Level = 12, RoleId = 2, Remove = false },
            new() { Level = 20, RoleId = 3, Remove = false },
        ];

        var changes = XpService.GetRoleRewardChanges(rewards, 4, 12);

        Assert.That(changes, Is.EquivalentTo(new Dictionary<ulong, bool> { [1] = true, [2] = false }));
        Assert.That(XpService.GetRoleRewardChanges(rewards, 12, 12), Is.Empty);
    }

    [Test]
    public async Task SetRoleReward_StopsAtTheLimitButStillUpdatesALevel()
    {
        for (var level = 1; level <= XpService.MAX_ROLE_REWARDS; level++)
            Assert.That(await _svc.SetRoleRewardAsync(GUILD_ID, level, 7, false), Is.True);

        Assert.That(await _svc.SetRoleRewardAsync(GUILD_ID, XpService.MAX_ROLE_REWARDS + 1, 7, false), Is.False);
        Assert.That(await _svc.SetRoleRewardAsync(GUILD_ID, 1, 8, true), Is.True);
        Assert.That(await _svc.SetRoleRewardAsync(GUILD_ID + 1, 1, 7, false), Is.True);

        var settings = await _svc.GetFullXpSettingsFor(GUILD_ID);
        Assert.That(settings.RoleRewards, Has.Count.EqualTo(XpService.MAX_ROLE_REWARDS));
        Assert.That(settings.RoleRewards, Has.Exactly(1).Matches<XpRoleReward>(static r => r.Level == 1 && r.RoleId == 8 && r.Remove));
    }
}
