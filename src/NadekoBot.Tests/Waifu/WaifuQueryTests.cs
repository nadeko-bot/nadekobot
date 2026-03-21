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
using OneOf;
using OneOf.Types;

namespace NadekoBot.Tests.Waifu;

[TestFixture]
public class WaifuQueryTests
{
    private WaifuService _svc = null!;
    private ICurrencyService _cs = null!;
    private IBotCache _cache = null!;
    private TestDbService _db = null!;

    private const double CYCLE_HOURS = 24.0;
    private const double BASE_RETURN_RATE = 0.17;
    private static readonly double CYCLES_PER_YEAR = 365.25 * 24 / CYCLE_HOURS;

    [SetUp]
    public void Setup()
    {
        _db = new TestDbService();
        _cs = Substitute.For<ICurrencyService>();
        _cache = Substitute.For<IBotCache>();
        var time = new FakeTimeProvider(new(2025, 1, 7, 0, 0, 0, TimeSpan.Zero));
        var client = Substitute.For<DiscordSocketClient>();
        _svc = new WaifuService(_db, _cache, _cs, client, WaifuTestHelper.CreateConfigService(), WaifuTestHelper.CreatePatronageService(), null!, time);
        _cs.RemoveAsync(Arg.Any<ulong>(), Arg.Any<long>(), Arg.Any<TxData?>()).Returns(true);
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    [Test]
    public async Task GetWaifuInfo_OptedIn_ReturnsFullDto()
    {
        await using (var ctx = _db.GetDbContext())
            await WaifuTestHelper.CreateWaifu(ctx, 1001, mood: 800, food: 600, fee: 3, price: 5000,
                managerId: 2001, returnsCap: 500_000, isHubby: true);

        var result = await _svc.GetWaifuInfoAsync(1001);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Mood, Is.EqualTo(800));
        Assert.That(result.Food, Is.EqualTo(600));
        Assert.That(result.WaifuFeePercent, Is.EqualTo(3));
        Assert.That(result.Price, Is.EqualTo(5000));
        Assert.That(result.ManagerId, Is.EqualTo(2001));
        Assert.That(result.IsHubby, Is.True);
    }

    [Test]
    public async Task GetWaifuInfo_NonExistent_ReturnsNull()
    {
        Assert.That(await _svc.GetWaifuInfoAsync(9999), Is.Null);
    }

    [Test]
    public async Task GetBacking_ActiveFan_ReturnsWaifuId()
    {
        await using (var ctx = _db.GetDbContext())
        {
            await WaifuTestHelper.CreateWaifu(ctx, 1001);
            await WaifuTestHelper.CreateFan(ctx, 2001, 1001);
        }

        Assert.That(await _svc.GetBackingAsync(2001), Is.EqualTo(1001));
        Assert.That(await _svc.GetBackingAsync(9999), Is.Null);
    }

    [Test]
    public async Task IsWaifu_IsFan_IsManager_ReturnCorrectBooleans()
    {
        await using (var ctx = _db.GetDbContext())
        {
            await WaifuTestHelper.CreateWaifu(ctx, 1001, managerId: 5001);
            await WaifuTestHelper.CreateFan(ctx, 2001, 1001);
        }

        Assert.That(await _svc.IsWaifuAsync(1001), Is.True);
        Assert.That(await _svc.IsWaifuAsync(9999), Is.False);
        Assert.That(await _svc.IsFanAsync(2001), Is.True);
        Assert.That(await _svc.IsFanAsync(9999), Is.False);
        Assert.That(await _svc.IsManagerAsync(5001), Is.True);
        Assert.That(await _svc.IsManagerAsync(9999), Is.False);
    }

    [Test]
    public async Task GetFans_ReturnsFanList()
    {
        await using (var ctx = _db.GetDbContext())
        {
            await WaifuTestHelper.CreateWaifu(ctx, 1001, managerId: 5001);
            await WaifuTestHelper.CreateFan(ctx, 2001, 1001);
            await WaifuTestHelper.CreateFan(ctx, 3001, 1001);
        }

        var result = await _svc.GetFansAsync(1001, 0, 10);
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result.Select(x => x.UserId), Is.EquivalentTo(new ulong[] { 2001, 3001 }));
    }

    [Test]
    public async Task GetLeaderboard_RankedByPrice()
    {
        var currentCycle = _svc.GetCurrentCycle();

        await using (var ctx = _db.GetDbContext())
        {
            await WaifuTestHelper.CreateWaifu(ctx, 1001, price: 5000);
            await WaifuTestHelper.CreateCycleRecord(ctx, 1001, currentCycle - 1, 5001,
                totalBacked: 10000);

            await WaifuTestHelper.CreateWaifu(ctx, 2001, price: 10000);
            await WaifuTestHelper.CreateCycleRecord(ctx, 2001, currentCycle - 1, 6001,
                totalBacked: 20000);
        }

        var result = await _svc.GetLeaderboardAsync(WaifuLbOrder.Price);
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result[0].UserId, Is.EqualTo(2001), "Higher price should be first");
        Assert.That(result[0].Price, Is.EqualTo(10000));
        Assert.That(result[1].Price, Is.EqualTo(5000));
    }

    [Test]
    public async Task GetRemainingActions_ReturnsCorrectCount()
    {
        _cache.GetAsync(Arg.Any<TypedKey<int>>())
            .Returns(new System.Threading.Tasks.ValueTask<OneOf<int, None>>(new None()));
        Assert.That(await _svc.GetRemainingActionsAsync(1001), Is.EqualTo(2));

        _cache.GetAsync(Arg.Any<TypedKey<int>>())
            .Returns(new System.Threading.Tasks.ValueTask<OneOf<int, None>>((OneOf<int, None>)1));
        Assert.That(await _svc.GetRemainingActionsAsync(1001), Is.EqualTo(1));
    }
}
