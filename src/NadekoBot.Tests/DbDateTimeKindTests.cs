using System;
using System.Linq;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Db.Models;
using NadekoBot.Tests.Waifu;
using NUnit.Framework;

namespace NadekoBot.Tests;

public class DbDateTimeKindTests
{
    private TestDbService _db = null!;

    [SetUp]
    public void Setup()
        => _db = new();

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    [Test]
    public async Task DatesReadBack_AreUtc_InLinqToDbAndEf()
    {
        var when = new DateTime(2026, 9, 29, 12, 30, 0, DateTimeKind.Utc);
        var added = when.AddDays(-1);

        await using (var ctx = _db.GetDbContext())
        {
            await ctx.GetTable<Reminder>()
                     .InsertAsync(() => new()
                     {
                         When = when,
                         DateAdded = added,
                         Message = "due",
                         ChannelId = 1,
                         GuildId = 1,
                         UserId = 1,
                         IsPrivate = false,
                         Type = ReminderType.User,
                     });

            await ctx.GetTable<Reminder>()
                     .InsertAsync(() => new()
                     {
                         When = when.AddDays(1),
                         DateAdded = null,
                         Message = "later",
                         ChannelId = 1,
                         GuildId = 1,
                         UserId = 1,
                         IsPrivate = false,
                         Type = ReminderType.User,
                     });
        }

        await using var uow = _db.GetDbContext();

        var l2db = await uow.GetTable<Reminder>()
                            .Where(x => x.When <= when)
                            .ToArrayAsyncLinqToDB();

        Assert.That(l2db.Select(x => x.Message), Is.EqualTo(new[] { "due" }));
        Assert.That(l2db[0].When, Is.EqualTo(when));
        Assert.That(l2db[0].When.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(l2db[0].DateAdded?.Kind, Is.EqualTo(DateTimeKind.Utc));

        var ef = await uow.Set<Reminder>()
                          .AsNoTracking()
                          .OrderBy(x => x.When)
                          .ToArrayAsyncEF();

        Assert.That(ef[0].When.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(ef[0].DateAdded, Is.EqualTo(added));
        Assert.That(ef[0].DateAdded?.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(ef[1].DateAdded, Is.Null);

        var projected = await uow.GetTable<Reminder>()
                                 .Select(x => x.When)
                                 .FirstAsyncLinqToDB();
        Assert.That(projected.Kind, Is.EqualTo(DateTimeKind.Utc));
    }
}
