using System.Linq;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Db.Models;
using NadekoBot.Services;
using NadekoBot.Services.Currency;
using NadekoBot.Tests.Waifu;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Gambling;

public class CurrencyTransferTests
{
    private const ulong GIVER = 1;
    private const ulong MEMBER_WITH_WALLET = 2;
    private const ulong NEW_MEMBER = 3;

    private TestDbService _db = null!;
    private CurrencyService _cs = null!;

    [SetUp]
    public void Setup()
    {
        _db = new TestDbService();
        _cs = new CurrencyService(_db, Substitute.For<ITxTracker>());
    }

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    [Test]
    public async Task TransferToMany_PaysEachMemberFromTheGiver_OrChangesNothing()
    {
        await using (var ctx = _db.GetDbContext())
        {
            await ctx.GetTable<DiscordUser>().InsertAsync(() => new() { UserId = GIVER, Username = "giver", CurrencyAmount = 250 });
            await ctx.GetTable<DiscordUser>().InsertAsync(() => new() { UserId = MEMBER_WITH_WALLET, Username = "member", CurrencyAmount = 10 });
        }

        ulong[] members = [MEMBER_WITH_WALLET, NEW_MEMBER];
        var fromTx = new TxData("gift", "@Role (2)");
        var toTx = new TxData("gift", "giver", null, GIVER);

        Assert.That(await _cs.TransferToManyAsync(GIVER, members, 100, fromTx, toTx), Is.True);
        Assert.That(await _cs.TransferToManyAsync(GIVER, members, 30, fromTx, toTx), Is.False);

        Assert.That(await _cs.GetBalanceAsync(GIVER), Is.EqualTo(50));
        Assert.That(await _cs.GetBalanceAsync(MEMBER_WITH_WALLET), Is.EqualTo(110));
        Assert.That(await _cs.GetBalanceAsync(NEW_MEMBER), Is.EqualTo(100));

        // the last recipient's row fails after the giver and the first recipient already changed
        await ExecAsync($"CREATE TRIGGER fail_tx BEFORE INSERT ON \"{TableName<CurrencyTransaction>()}\" "
                        + $"WHEN NEW.UserId = {NEW_MEMBER} BEGIN SELECT RAISE(ABORT, 'forced'); END;");
        Assert.CatchAsync(() => _cs.TransferToManyAsync(GIVER, members, 20, fromTx, toTx));
        await ExecAsync("DROP TRIGGER fail_tx;");

        Assert.That(await _cs.GetBalanceAsync(GIVER), Is.EqualTo(50));
        Assert.That(await _cs.GetBalanceAsync(MEMBER_WITH_WALLET), Is.EqualTo(110));

        await using var readCtx = _db.GetDbContext();
        var txs = await readCtx.GetTable<CurrencyTransaction>()
            .OrderBy(x => x.UserId)
            .Select(x => new { x.UserId, x.Amount })
            .ToListAsyncLinqToDB();

        Assert.That(txs.Select(x => (x.UserId, x.Amount)),
            Is.EqualTo(new[] { (GIVER, -200L), (MEMBER_WITH_WALLET, 100L), (NEW_MEMBER, 100L) }));
    }

    private string TableName<T>()
    {
        using var ctx = _db.GetDbContext();
        return ctx.Model.FindEntityType(typeof(T))!.GetTableName()!;
    }

    private async Task ExecAsync(string sql)
    {
        await using var ctx = _db.GetDbContext();
        await ctx.Database.ExecuteSqlRawAsync(sql);
    }
}
