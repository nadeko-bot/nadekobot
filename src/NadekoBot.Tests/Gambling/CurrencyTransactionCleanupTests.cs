using System;
using System.Linq;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Db.Models;
using NadekoBot.Tests.Waifu;
using NUnit.Framework;

namespace NadekoBot.Tests;

public class CurrencyTransactionCleanupTests
{
    private const int LIFETIME_DAYS = 30;

    private TestDbService _db = null!;

    [SetUp]
    public void Setup()
        => _db = new();

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    [Test]
    public async Task Cleanup_RemovesOnlyTransactionsPastTheirLifetime()
    {
        var now = DateTime.UtcNow;

        await SeedAsync("fresh", now.AddDays(-1));
        await SeedAsync("yesterday_of_the_window", now.AddDays(-LIFETIME_DAYS + 1));
        await SeedAsync("expired", now.AddDays(-LIFETIME_DAYS - 1));
        await SeedAsync("ancient", now.AddDays(-100));
        await SeedAsync("undated", null);

        await using var uow = _db.GetDbContext();

        var cutoff = now - TimeSpan.FromDays(LIFETIME_DAYS);
        await uow.GetTable<CurrencyTransaction>()
                 .Where(ct => ct.DateAdded == null || ct.DateAdded < cutoff)
                 .DeleteAsync();

        var left = await uow.GetTable<CurrencyTransaction>()
                            .Select(x => x.Type)
                            .ToListAsyncLinqToDB();

        // a transaction inside its lifetime has to survive, or .curtrs shows nothing
        Assert.That(left, Is.EquivalentTo(new[] { "fresh", "yesterday_of_the_window" }));
    }

    private async Task SeedAsync(string type, DateTime? dateAdded)
    {
        await using var uow = _db.GetDbContext();
        await uow.GetTable<CurrencyTransaction>()
                 .InsertAsync(() => new()
                 {
                     Amount = 1,
                     UserId = 1,
                     Type = type,
                     Extra = "",
                     Note = "",
                     DateAdded = dateAdded
                 });
    }
}
