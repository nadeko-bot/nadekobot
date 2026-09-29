using System.Linq;
using System.Threading.Tasks;
using Discord.WebSocket;
using Nadeko.Common;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Db.Models;
using NadekoBot.Extensions;
using NadekoBot.Modules.Gambling.Bank;
using NadekoBot.Modules.Games.Quests;
using NadekoBot.Services;
using NadekoBot.Services.Currency;
using NadekoBot.Tests.Waifu;
using NSubstitute;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace NadekoBot.Tests.Gambling;

public class BankServiceTests
{
    private const ulong USER_ID = 42;

    private TestDbService _db = null!;
    private BankService _svc = null!;

    [SetUp]
    public void Setup()
    {
        _db = new TestDbService();

        var quests = new QuestService(_db,
            Substitute.For<IBotCache>(),
            Substitute.For<IMessageSenderService>(),
            Substitute.For<DiscordSocketClient>());

        _svc = new BankService(_db, Substitute.For<ITxTracker>(), quests);
    }

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    [Test]
    public async Task DepositAndWithdraw_MoveCurrencyBetweenWalletAndBank()
    {
        await SeedWalletAsync(100);

        Assert.That(await _svc.DepositAsync(USER_ID, 70), Is.True);
        Assert.That(await _svc.WithdrawAsync(USER_ID, 20), Is.True);

        Assert.That(await GetWalletAsync(), Is.EqualTo(50));
        Assert.That(await _svc.GetBalanceAsync(USER_ID), Is.EqualTo(50));

        await using var ctx = _db.GetDbContext();
        var txSum = await ctx.GetTable<CurrencyTransaction>()
            .Where(x => x.UserId == USER_ID && x.Type == "bank")
            .SumAsyncLinqToDB(x => x.Amount);
        Assert.That(txSum, Is.EqualTo(-50));
    }

    [Test]
    public async Task InsufficientFunds_ChangeNothing()
    {
        await SeedWalletAsync(30);

        Assert.That(await _svc.DepositAsync(USER_ID, 31), Is.False);
        Assert.That(await _svc.WithdrawAsync(USER_ID, 1), Is.False);
        Assert.That(await _svc.DepositAsync(USER_ID, 30), Is.True);
        Assert.That(await _svc.WithdrawAsync(USER_ID, 31), Is.False);

        Assert.That(await GetWalletAsync(), Is.EqualTo(0));
        Assert.That(await _svc.GetBalanceAsync(USER_ID), Is.EqualTo(30));
    }

    [Test]
    public async Task FailureMidway_RollsBackWalletBankAndTransactions()
    {
        await SeedWalletAsync(100);
        Assert.That(await _svc.DepositAsync(USER_ID, 60), Is.True);

        // withdraw fails on its last step, after the bank and wallet rows already changed
        await ExecAsync($"CREATE TRIGGER fail_tx BEFORE INSERT ON \"{TableName<CurrencyTransaction>()}\" "
                        + "BEGIN SELECT RAISE(ABORT, 'forced'); END;");
        Assert.CatchAsync(() => _svc.WithdrawAsync(USER_ID, 50));
        await ExecAsync("DROP TRIGGER fail_tx;");

        // deposit fails on its last step, after the wallet row and transaction row already changed
        var bank = TableName<BankUser>();
        await ExecAsync($"CREATE TRIGGER fail_bank_upd BEFORE UPDATE ON \"{bank}\" BEGIN SELECT RAISE(ABORT, 'forced'); END;");
        await ExecAsync($"CREATE TRIGGER fail_bank_ins BEFORE INSERT ON \"{bank}\" BEGIN SELECT RAISE(ABORT, 'forced'); END;");
        Assert.CatchAsync(() => _svc.DepositAsync(USER_ID, 30));

        Assert.That(await GetWalletAsync(), Is.EqualTo(40));
        Assert.That(await _svc.GetBalanceAsync(USER_ID), Is.EqualTo(60));

        await using var ctx = _db.GetDbContext();
        var txCount = await ctx.GetTable<CurrencyTransaction>()
            .Where(x => x.UserId == USER_ID)
            .CountAsyncLinqToDB();
        Assert.That(txCount, Is.EqualTo(1));
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

    private async Task SeedWalletAsync(long amount)
    {
        await using var ctx = _db.GetDbContext();
        await ctx.GetTable<DiscordUser>()
            .InsertAsync(() => new()
            {
                UserId = USER_ID,
                Username = "user",
                CurrencyAmount = amount
            });
    }

    private async Task<long> GetWalletAsync()
    {
        await using var ctx = _db.GetDbContext();
        return await ctx.GetTable<DiscordUser>()
            .Where(x => x.UserId == USER_ID)
            .Select(x => x.CurrencyAmount)
            .FirstAsyncLinqToDB();
    }
}
