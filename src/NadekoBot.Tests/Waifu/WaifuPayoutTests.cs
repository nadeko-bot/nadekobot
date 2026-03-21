#nullable enable
using System;
using System.Threading.Tasks;
using Discord.WebSocket;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Nadeko.Common;
using NadekoBot.Modules.Waifus.Waifu;
using NadekoBot.Modules.Waifus.Waifu.Db;
using NadekoBot.Services;
using NadekoBot.Services.Currency;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Waifu;

[TestFixture]
public class WaifuPayoutTests
{
    private WaifuService _svc = null!;
    private ICurrencyService _cs = null!;
    private TestDbService _db = null!;
    private FakeTimeProvider _time = null!;

    private static readonly DateTime Epoch = new(2025, 1, 7, 0, 0, 0, DateTimeKind.Utc);

    [SetUp]
    public void Setup()
    {
        _db = new TestDbService();
        _cs = Substitute.For<ICurrencyService>();
        var cache = Substitute.For<IBotCache>();
        _time = new FakeTimeProvider(new(Epoch, TimeSpan.Zero));
        var client = Substitute.For<DiscordSocketClient>();
        _svc = new WaifuService(_db, cache, _cs, client, WaifuTestHelper.CreateConfigService(), WaifuTestHelper.CreatePatronageService(), null!, _time);
        _cs.RemoveAsync(Arg.Any<ulong>(), Arg.Any<long>(), Arg.Any<TxData?>()).Returns(true);
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    [Test]
    public async Task GetPendingPayout_NoPendingRow_ReturnsZero()
    {
        var pending = await _svc.GetPendingPayoutAsync(9999);
        Assert.That(pending, Is.EqualTo(0));
    }

    [Test]
    public async Task ClaimPayout_NoPending_ReturnsError()
    {
        var result = await _svc.ClaimPayoutAsync(9999);
        Assert.That(result.IsT0, Is.True);
    }

    [Test]
    public async Task ClaimPayout_FloorsAndDeletesRow()
    {
        await using (var ctx = _db.GetDbContext())
        {
            await ctx.GetTable<WaifuPendingPayout>()
                .InsertAsync(() => new WaifuPendingPayout
                {
                    UserId = 1001,
                    Amount = 50.3m
                });
        }

        var result = await _svc.ClaimPayoutAsync(1001);
        Assert.That(result.AsT1.Value, Is.EqualTo(50));
        await _cs.Received(1).AddAsync(1001, 50, Arg.Any<TxData?>());

        await using (var ctx = _db.GetDbContext())
        {
            var row = await ctx.GetTable<WaifuPendingPayout>()
                .FirstOrDefaultAsyncLinqToDB(x => x.UserId == 1001);
            Assert.That(row, Is.Null);
        }
    }

    [Test]
    public async Task ClaimPayout_LessThanOne_ReturnsError()
    {
        await using (var ctx = _db.GetDbContext())
        {
            await ctx.GetTable<WaifuPendingPayout>()
                .InsertAsync(() => new WaifuPendingPayout
                {
                    UserId = 1001,
                    Amount = 0.5m
                });
        }

        var result = await _svc.ClaimPayoutAsync(1001);
        Assert.That(result.IsT0, Is.True);
        await _cs.DidNotReceive().AddAsync(Arg.Any<ulong>(), Arg.Any<long>(), Arg.Any<TxData?>());
    }

    [Test]
    public async Task AddPendingPayout_AccumulatesFractionalDecimals()
    {
        await using (var ctx = _db.GetDbContext())
        {
            await _svc.AddPendingPayoutInternalAsync(ctx, 1001, 100.25m);
            await _svc.AddPendingPayoutInternalAsync(ctx, 1001, 50.50m);
            await _svc.AddPendingPayoutInternalAsync(ctx, 1001, 25.10m);
        }

        await using (var ctx = _db.GetDbContext())
        {
            var row = await ctx.GetTable<WaifuPendingPayout>()
                .FirstAsyncLinqToDB(x => x.UserId == 1001);
            Assert.That(row.Amount, Is.EqualTo(175.85m));
        }

        var pending = await _svc.GetPendingPayoutAsync(1001);
        Assert.That(pending, Is.EqualTo(175));
    }

    [Test]
    public async Task CycleProcessing_WritesFractionalPendingPayouts()
    {
        var cycleNumber = _svc.GetCurrentCycle();

        await using (var ctx = _db.GetDbContext())
        {
            await WaifuTestHelper.CreateWaifu(ctx, 1001, mood: 1000, food: 1000, fee: 5,
                managerId: 2001, returnsCap: 500_000_000);
            await WaifuTestHelper.CreateFan(ctx, 2001, 1001);
            await WaifuTestHelper.SetBankBalance(ctx, 2001, 100_000_000);
        }

        await _svc.SnapshotCycleInternalAsync(cycleNumber);
        await _svc.ProcessCycleInternalAsync(cycleNumber);

        await _cs.DidNotReceive().AddAsync(Arg.Any<ulong>(), Arg.Any<long>(), Arg.Any<TxData?>());

        await using (var ctx = _db.GetDbContext())
        {
            var waifuPending = await ctx.GetTable<WaifuPendingPayout>()
                .FirstAsyncLinqToDB(x => x.UserId == 1001);
            Assert.That(waifuPending.Amount, Is.Not.EqualTo(Math.Floor(waifuPending.Amount)),
                "Waifu payout should have fractional component");

            var managerPending = await ctx.GetTable<WaifuPendingPayout>()
                .FirstAsyncLinqToDB(x => x.UserId == 2001);
            Assert.That(managerPending.Amount, Is.Not.EqualTo(Math.Floor(managerPending.Amount)),
                "Manager payout should have fractional component");
        }
    }
}
