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
        _svc = new WaifuService(_db, cache, _cs, client, WaifuTestHelper.CreateConfigService(), WaifuTestHelper.CreatePatronageService(), null!, null!, null!, _time);
        _cs.RemoveAsync(Arg.Any<ulong>(), Arg.Any<long>(), Arg.Any<TxData?>()).Returns(true);
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    [Test]
    public async Task PendingPayout_AccumulatesFractions_ClaimFloorsDeletesRowAndRefusesLessThanOne()
    {
        Assert.That(await _svc.GetPendingPayoutAsync(9999), Is.EqualTo(0));
        Assert.That((await _svc.ClaimPayoutAsync(9999)).IsT0, Is.True);

        await using (var ctx = _db.GetDbContext())
        {
            await ctx.GetTable<WaifuPendingPayout>()
                .InsertAsync(() => new WaifuPendingPayout
                {
                    UserId = 1002,
                    Amount = 0.5m
                });
        }

        var tooSmall = await _svc.ClaimPayoutAsync(1002);
        Assert.That(tooSmall.IsT0, Is.True);
        await _cs.DidNotReceive().AddAsync(Arg.Any<ulong>(), Arg.Any<long>(), Arg.Any<TxData?>());

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

        Assert.That(await _svc.GetPendingPayoutAsync(1001), Is.EqualTo(175));

        var result = await _svc.ClaimPayoutAsync(1001);
        Assert.That(result.AsT1.Value, Is.EqualTo(175));
        await _cs.Received(1).AddAsync(1001, 175, Arg.Any<TxData?>());

        await using (var ctx = _db.GetDbContext())
        {
            var row = await ctx.GetTable<WaifuPendingPayout>()
                .FirstOrDefaultAsyncLinqToDB(x => x.UserId == 1001);
            Assert.That(row, Is.Null);
        }
    }
}
