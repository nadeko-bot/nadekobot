#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Discord.WebSocket;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Nadeko.Common;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Waifus.Waifu;
using NadekoBot.Modules.Waifus.Waifu.Db;
using NadekoBot.Services;
using NadekoBot.Services.Currency;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Waifu;

[TestFixture]
public class WaifuIntegrationTests
{
    private WaifuService _svc = null!;
    private ICurrencyService _cs = null!;
    private TestDbService _db = null!;

    [SetUp]
    public void Setup()
    {
        _db = new TestDbService();
        _cs = Substitute.For<ICurrencyService>();
        var cache = Substitute.For<IBotCache>();
        var time = new FakeTimeProvider(new(2025, 1, 7, 0, 0, 0, TimeSpan.Zero));
        var client = Substitute.For<DiscordSocketClient>();
        _svc = new WaifuService(_db, cache, _cs, client, WaifuTestHelper.CreateConfigService(), WaifuTestHelper.CreatePatronageService(), null!, time);
        _cs.RemoveAsync(Arg.Any<ulong>(), Arg.Any<long>(), Arg.Any<TxData?>()).Returns(true);
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    [Test]
    public async Task FullLifecycle_OptInFanManagerCyclePayout()
    {
        // Opt in
        var optIn = await _svc.OptInAsync(1001);
        Assert.That(optIn.IsT2, Is.True);

        // Fans join
        await _svc.BecomeFanAsync(2001, 1001);
        await _svc.BecomeFanAsync(3001, 1001);

        // Buy manager - opt-in price was 10000, so waifu price = 10000/10 = 1000
        // Required = ceil(1000 * 1.15) = 1150
        var buy = await _svc.BuyManagerAsync(3001, 1001, 1150);
        Assert.That(buy.IsT5, Is.True);

        // Boost stats and set bank balances for meaningful cycle payouts
        await using (var ctx = _db.GetDbContext())
        {
            await ctx.GetTable<WaifuInfo>().Where(x => x.UserId == 1001)
                .Set(x => x.ReturnsCap, 500_000_000L)
                .Set(x => x.Mood, 1000)
                .Set(x => x.Food, 1000)
                .UpdateAsync();
            await WaifuTestHelper.SetBankBalance(ctx, 2001, 60_000_000);
            await WaifuTestHelper.SetBankBalance(ctx, 3001, 40_000_000);
        }

        // Snapshot and process cycle
        var cycleNumber = _svc.GetCurrentCycle();
        await _svc.SnapshotCycleInternalAsync(cycleNumber);
        _cs.ClearReceivedCalls();

        await _svc.ProcessCycleInternalAsync(cycleNumber);

        // Verify cycle record created with non-zero returns
        await using (var ctx = _db.GetDbContext())
        {
            var cycle = await ctx.GetTable<WaifuCycle>()
                .FirstOrDefaultAsyncLinqToDB(x => x.WaifuUserId == 1001 && x.CycleNumber == cycleNumber);
            Assert.That(cycle, Is.Not.Null);
            Assert.That(cycle!.TotalBacked, Is.EqualTo(100_000_000));

            var wi = await ctx.GetTable<WaifuInfo>().FirstAsyncLinqToDB(x => x.UserId == 1001);
            Assert.That(wi.TotalProduced, Is.GreaterThan(0));
        }
    }

    [Test]
    public async Task ManagerResign_ThenNewManagerBuysAtReducedPrice()
    {
        await using (var ctx = _db.GetDbContext())
        {
            await WaifuTestHelper.CreateWaifu(ctx, 1001, price: 10_000, managerId: 3001);
            await WaifuTestHelper.CreateFan(ctx, 3001, 1001);
            await ctx.GetTable<WaifuFan>()
                .InsertAsync(() => new WaifuFan { UserId = 2001, WaifuUserId = 1001, DelegatedAt = DateTime.UtcNow });
            await WaifuTestHelper.SetBankBalance(ctx, 2001, 5_000);
        }

        // No money should be created on resign
        _cs.ClearReceivedCalls();

        // Manager resigns -> price drops to MinPrice (1000), no refund
        await _svc.ResignManagerAsync(3001, 1001);
        await _cs.DidNotReceive().AddAsync(Arg.Any<ulong>(), Arg.Any<long>(), Arg.Any<TxData?>());

        await using (var ctx = _db.GetDbContext())
        {
            var wi = await ctx.GetTable<WaifuInfo>().FirstAsyncLinqToDB(x => x.UserId == 1001);
            Assert.That(wi.Price, Is.EqualTo(1000));
            Assert.That(wi.ManagerUserId, Is.Null);
        }

        // User B buys at reduced price: ceil(1000*1.15)=1150
        _cs.ClearReceivedCalls();
        var buy = await _svc.BuyManagerAsync(2001, 1001, 1150);
        Assert.That(buy.IsT5, Is.True);
        Assert.That(buy.AsT5.Value.PricePaid, Is.EqualTo(1150));

        await using (var ctx = _db.GetDbContext())
        {
            var wi = await ctx.GetTable<WaifuInfo>().FirstAsyncLinqToDB(x => x.UserId == 1001);
            Assert.That(wi.ManagerUserId, Is.EqualTo(2001));
        }
    }
}
