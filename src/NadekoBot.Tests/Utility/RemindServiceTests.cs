using System;
using System.Threading.Tasks;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Utility.Services;
using NadekoBot.Tests.Waifu;
using NUnit.Framework;

namespace NadekoBot.Tests.Utility;

public class RemindServiceTests
{
    private const ulong GUILD_ID = 111;
    private const ulong USER_ID = 444;

    private TestDbService _db = null!;
    private RemindService _svc = null!;

    [SetUp]
    public void Setup()
    {
        _db = new TestDbService();
        _svc = new RemindService(null!, _db, null!, null!);
    }

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    private Task<bool> AddAsync(ulong userId, ulong guildId, ReminderType type = ReminderType.User)
        => _svc.AddReminderAsync(userId, 1, guildId, false, DateTime.UtcNow.AddDays(1), "hi", type);

    [Test]
    public async Task Add_StopsAtTheMemberAndServerLimits()
    {
        for (var i = 0; i < RemindService.MAX_REMINDERS_PER_USER; i++)
            Assert.That(await AddAsync(USER_ID, GUILD_ID), Is.True);

        Assert.That(await AddAsync(USER_ID, GUILD_ID), Is.False);
        Assert.That(await AddAsync(USER_ID, GUILD_ID, ReminderType.Timely), Is.True);

        var added = RemindService.MAX_REMINDERS_PER_USER;
        for (var user = 1UL; added < RemindService.MAX_REMINDERS_PER_SERVER; user++)
        {
            for (var i = 0; i < RemindService.MAX_REMINDERS_PER_USER && added < RemindService.MAX_REMINDERS_PER_SERVER; i++, added++)
                Assert.That(await AddAsync(user, GUILD_ID), Is.True);
        }

        Assert.That(await AddAsync(9999, GUILD_ID), Is.False);
        Assert.That(await AddAsync(9999, GUILD_ID + 1), Is.True);
    }

    // Every DM reminder has server id 0, so a server limit there would let a few members block DM reminders for all.
    [Test]
    public async Task DmReminders_HaveOnlyTheMemberLimit()
    {
        var added = 0;
        for (var user = 1UL; added <= RemindService.MAX_REMINDERS_PER_SERVER; user++)
        {
            for (var i = 0; i < RemindService.MAX_REMINDERS_PER_USER; i++, added++)
                Assert.That(await AddAsync(user, 0), Is.True);

            Assert.That(await AddAsync(user, 0), Is.False);
        }
    }
}
