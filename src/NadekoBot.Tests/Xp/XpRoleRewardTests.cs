#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Xp.Services;
using NadekoBot.Tests.Waifu;
using NUnit.Framework;

namespace NadekoBot.Tests.Xp;

public sealed class XpRoleRewardTests
{
    private const ulong GUILD_ID = 111;
    private const ulong MEMBER_ROLE = 201;
    private const ulong NEWBIE_ROLE = 202;

    private TestDbService _db = null!;
    private XpService _svc = null!;

    [SetUp]
    public void SetUp()
    {
        _db = new TestDbService();
        _svc = new XpService(null!, _db, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, NadekoBot.Common.IntentState.All);
    }

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    private async Task<(int Level, ulong RoleId, bool Remove)[]> GetRewardsAsync()
    {
        var settings = await _svc.GetFullXpSettingsFor(GUILD_ID);
        return settings.RoleRewards
                       .OrderBy(x => x.Level)
                       .ThenBy(x => x.RoleId)
                       .Select(x => (x.Level, x.RoleId, x.Remove))
                       .ToArray();
    }

    [Test]
    public void LevelUp_ChangesEachRoleOnceAndTheHighestPassedLevelWins()
    {
        XpRoleReward[] rewards =
        [
            new() { Level = 3, RoleId = 4, Remove = false },
            new() { Level = 5, RoleId = 1, Remove = false },
            new() { Level = 5, RoleId = 5, Remove = false },
            new() { Level = 10, RoleId = 1, Remove = true },
            new() { Level = 7, RoleId = 2, Remove = false },
            new() { Level = 12, RoleId = 2, Remove = false },
            new() { Level = 20, RoleId = 3, Remove = false },
        ];

        var changes = XpService.GetRoleRewardChanges(rewards, 4, 12);

        Assert.That(changes, Is.EquivalentTo(new Dictionary<ulong, bool> { [1] = true, [2] = false, [5] = false }));
        Assert.That(XpService.GetRoleRewardChanges(rewards, 12, 12), Is.Empty);
    }

    [Test]
    public async Task SetRoleReward_KeepsAddAndRemoveAtOneLevel_AndUpdatesSameRole()
    {
        await _svc.SetRoleRewardAsync(GUILD_ID, 5, MEMBER_ROLE, false);
        await _svc.SetRoleRewardAsync(GUILD_ID, 5, NEWBIE_ROLE, true);

        Assert.That(await GetRewardsAsync(), Is.EqualTo(new[]
        {
            (5, MEMBER_ROLE, false),
            (5, NEWBIE_ROLE, true),
        }));

        await _svc.SetRoleRewardAsync(GUILD_ID, 5, MEMBER_ROLE, true);

        Assert.That(await GetRewardsAsync(), Is.EqualTo(new[]
        {
            (5, MEMBER_ROLE, true),
            (5, NEWBIE_ROLE, true),
        }));

        // the dashboard's level route keeps only the role it sets
        Assert.That(await _svc.ReplaceLevelRoleRewardAsync(GUILD_ID, 5, NEWBIE_ROLE, false), Is.True);
        Assert.That(await GetRewardsAsync(), Is.EqualTo(new[] { (5, NEWBIE_ROLE, false) }));
    }

    [Test]
    public async Task SetRoleReward_StopsAtTheLimitButStillUpdatesAReward()
    {
        for (var level = 1; level <= XpService.MAX_ROLE_REWARDS; level++)
            Assert.That(await _svc.SetRoleRewardAsync(GUILD_ID, level, MEMBER_ROLE, false), Is.True);

        Assert.That(await _svc.SetRoleRewardAsync(GUILD_ID, XpService.MAX_ROLE_REWARDS + 1, MEMBER_ROLE, false), Is.False);
        Assert.That(await _svc.SetRoleRewardAsync(GUILD_ID, 1, NEWBIE_ROLE, false), Is.False);
        Assert.That(await _svc.SetRoleRewardAsync(GUILD_ID, 1, MEMBER_ROLE, true), Is.True);
        Assert.That(await _svc.SetRoleRewardAsync(GUILD_ID + 1, 1, MEMBER_ROLE, false), Is.True);

        var rewards = await GetRewardsAsync();
        Assert.That(rewards, Has.Length.EqualTo(XpService.MAX_ROLE_REWARDS));
        Assert.That(rewards[0], Is.EqualTo((1, MEMBER_ROLE, true)));
    }

    [Test]
    public async Task ResetRoleReward_ClearsOneRoleOrTheWholeLevel_OnlyInThatGuild()
    {
        const ulong otherGuildId = 222;

        await _svc.SetRoleRewardAsync(GUILD_ID, 5, MEMBER_ROLE, false);
        await _svc.SetRoleRewardAsync(GUILD_ID, 5, NEWBIE_ROLE, true);
        await _svc.SetRoleRewardAsync(GUILD_ID, 10, MEMBER_ROLE, true);
        await _svc.SetRoleRewardAsync(otherGuildId, 5, MEMBER_ROLE, false);

        Assert.That(await _svc.ResetRoleRewardAsync(GUILD_ID, 5, NEWBIE_ROLE), Is.True);
        Assert.That(await _svc.ResetRoleRewardAsync(GUILD_ID, 5, NEWBIE_ROLE), Is.False);
        Assert.That(await GetRewardsAsync(), Is.EqualTo(new[]
        {
            (5, MEMBER_ROLE, false),
            (10, MEMBER_ROLE, true),
        }));

        await _svc.SetRoleRewardAsync(GUILD_ID, 5, NEWBIE_ROLE, true);
        Assert.That(await _svc.ResetRoleRewardAsync(GUILD_ID, 5), Is.EqualTo(2));
        Assert.That(await GetRewardsAsync(), Is.EqualTo(new[] { (10, MEMBER_ROLE, true) }));

        await using var ctx = _db.GetDbContext();
        var total = await ctx.GetTable<XpRoleReward>().CountAsyncLinqToDB();
        Assert.That(total, Is.EqualTo(2));
    }
}
