#nullable enable
using System;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Tests.Waifu;
using System.Text.Json;
using NadekoBot.Hosted;
using NadekoBot.Hosted.Endpoints;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Xp;
using NUnit.Framework;

namespace NadekoBot.Tests;

[TestFixture]
public sealed class ApiWarnXpTests
{
    private TestDbService? _db;

    [SetUp]
    public void SetUp()
        => _db = new TestDbService();

    [TearDown]
    public void TearDown()
        => _db?.Dispose();

    [Test]
    public async Task ReadRewards_IsSortedByLevelAndInsertsNothingForANewGuild()
    {
        var none = await XpEndpoints.ReadRewardsAsync(_db!, 1);
        Assert.That(none.RoleRewards, Is.Empty);
        Assert.That(none.CurrencyRewards, Is.Empty);

        await using (var ctx = _db!.GetDbContext())
        {
            Assert.That(await ctx.GetTable<XpSettings>().CountAsyncLinqToDB(), Is.Zero);

            var id = await ctx.GetTable<XpSettings>().InsertWithInt32IdentityAsync(() => new XpSettings { GuildId = 2, XpFormulaA = XpSettings.DEFAULT_FORMULA_A, XpFormulaC = XpSettings.DEFAULT_FORMULA_C });
            await ctx.GetTable<XpRoleReward>().InsertAsync(() => new XpRoleReward { XpSettingsId = id, Level = 20, RoleId = 7, Remove = false });
            await ctx.GetTable<XpRoleReward>().InsertAsync(() => new XpRoleReward { XpSettingsId = id, Level = 5, RoleId = 8, Remove = true });
            await ctx.GetTable<XpCurrencyReward>().InsertAsync(() => new XpCurrencyReward { XpSettingsId = id, Level = 10, Amount = 100 });
        }

        var rewards = await XpEndpoints.ReadRewardsAsync(_db, 2);
        Assert.That(rewards.RoleRewards, Is.EqualTo(new[] { new Hosted.XpRoleRewardDto(5, "8", true), new Hosted.XpRoleRewardDto(20, "7", false) }));
        Assert.That(rewards.CurrencyRewards, Is.EqualTo(new[] { new Hosted.XpCurrencyRewardDto(10, 100) }));
    }

    [Test]
    public void Warns_UseTheWireFormatTheDashboardReadsAndAcceptWhatWarnPunishAccepts()
    {
        var levels = new NadekoBot.Db.LevelStats(1000, 9, 27);
        var xpUser = XpEndpoints.ToXpUserDto(new UserXpStats { UserId = 123456789012345678UL, GuildId = 1, Xp = 1000 },
            new XpFormula(9, 27), rank: 7, username: "bob");
        Assert.That((xpUser.Rank, xpUser.UserId, xpUser.Username, xpUser.Level, xpUser.LevelXp, xpUser.RequiredXp, xpUser.TotalXp),
            Is.EqualTo((7, "123456789012345678", "bob", levels.Level, levels.LevelXp, levels.RequiredXp, 1000L)));

        var warn = new Warning
        {
            Id = 5,
            UserId = 123456789012345678UL,
            Reason = "spam",
            Moderator = "admin",
            Weight = 3,
            DateAdded = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Unspecified)
        };

        var json = JsonSerializer.Serialize(WarnEndpoints.ToWarningDto(warn), ApiJsonContext.Default.WarningDto);
        Assert.That(json, Does.Contain("\"userId\":\"123456789012345678\""));
        Assert.That(json, Does.Contain("\"dateAdded\":\"2024-01-02T03:04:05Z\""));

        warn.DateAdded = null;
        Assert.That(WarnEndpoints.ToWarningDto(warn).DateAdded, Is.Null);

        var withRole = new WarningPunishment
        {
            Count = 4,
            Punishment = PunishmentAction.AddRole,
            Time = 30,
            RoleId = 987654321098765432UL
        };
        Assert.That(WarnEndpoints.ToWarnPunishmentDto(withRole),
            Is.EqualTo(new WarnPunishmentDto(4, "addrole", 30, "987654321098765432")));

        Assert.That(WarnEndpoints.TryParsePunishment("BAN", out var ban), Is.True);
        Assert.That(ban, Is.EqualTo(PunishmentAction.Ban));
        Assert.That(WarnEndpoints.TryParsePunishment("removeroles", out var removeRoles), Is.True);
        Assert.That(removeRoles, Is.EqualTo(PunishmentAction.RemoveRoles));

        foreach (var input in new[] { "", "nope", "99", "kick,ban", " ban", "warn" })
            Assert.That(WarnEndpoints.TryParsePunishment(input, out _), Is.False, input);
    }
}
