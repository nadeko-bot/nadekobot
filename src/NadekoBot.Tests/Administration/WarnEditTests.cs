using System;
using System.Linq;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Administration.Services;
using NadekoBot.Tests.Waifu;
using NUnit.Framework;

namespace NadekoBot.Tests.Administration;

public class WarnEditTests
{
    private const ulong GUILD_ID = 10;
    private const ulong USER_ID = 100;
    private const ulong OTHER_USER_ID = 200;

    private TestDbService _db = null!;
    private UserPunishService _svc = null!;

    [SetUp]
    public void Setup()
    {
        _db = new TestDbService();
        _svc = new UserPunishService(null!, _db, null!, null!, null!, null!, null!);
    }

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    private async Task AddWarningAsync(ulong userId, string reason, int daysAgo)
    {
        await using var ctx = _db.GetDbContext();
        await ctx.GetTable<Warning>()
                 .InsertAsync(() => new()
                 {
                     GuildId = GUILD_ID,
                     UserId = userId,
                     Reason = reason,
                     Forgiven = false,
                     Moderator = "mod",
                     Weight = 1,
                     DateAdded = DateTime.UtcNow.AddDays(-daysAgo),
                 });
    }

    [Test]
    public async Task WarnEdit_ChangesOnlyTheWarningAtTheWarnlogIndex()
    {
        await AddWarningAsync(USER_ID, "oldest", 3);
        await AddWarningAsync(USER_ID, "middle", 2);
        await AddWarningAsync(USER_ID, "newest", 1);
        await AddWarningAsync(OTHER_USER_ID, "other", 0);

        Assert.That(await _svc.WarnEditAsync(GUILD_ID, USER_ID, 1, "fixed"), Is.True);
        Assert.That(await _svc.WarnEditAsync(GUILD_ID, USER_ID, 3, "nothing"), Is.False);
        Assert.That(await _svc.WarnEditAsync(GUILD_ID + 1, USER_ID, 0, "nothing"), Is.False);

        await using var ctx = _db.GetDbContext();
        var reasons = await ctx.GetTable<Warning>()
                               .OrderByDescending(x => x.DateAdded)
                               .Select(x => x.Reason)
                               .ToArrayAsyncLinqToDB();

        Assert.That(reasons, Is.EqualTo(new[] { "other", "newest", "fixed", "oldest" }));
    }
}
