#nullable enable
using System;
using System.Linq;
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
public class WaifuBackingTests
{
    private WaifuService _svc = null!;
    private TestDbService _db = null!;
    private FakeTimeProvider _time = null!;

    [SetUp]
    public void Setup()
    {
        _db = new TestDbService();
        var cs = Substitute.For<ICurrencyService>();
        var cache = Substitute.For<IBotCache>();
        _time = new FakeTimeProvider(new(2025, 1, 7, 0, 0, 0, TimeSpan.Zero));
        var client = Substitute.For<DiscordSocketClient>();
        _svc = new WaifuService(_db, cache, cs, client, WaifuTestHelper.CreateConfigService(), WaifuTestHelper.CreatePatronageService(), null!, _time);
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    [Test]
    public async Task BecomeFan_Success_CreatesActiveFanRecord()
    {
        await using (var ctx = _db.GetDbContext())
            await WaifuTestHelper.CreateWaifu(ctx, 1001);

        var result = await _svc.BecomeFanAsync(2001, 1001);
        Assert.That(result.IsT3, Is.True, "Expected Success");

        await using (var ctx = _db.GetDbContext())
        {
            var fan = await ctx.GetTable<WaifuFan>().FirstOrDefaultAsyncLinqToDB(x => x.UserId == 2001);
            Assert.That(fan, Is.Not.Null);
            Assert.That(fan!.WaifuUserId, Is.EqualTo(1001));
        }
    }

    [Test]
    public async Task BecomeFan_SwitchWaifu_DeletesOldCreatesNew()
    {
        await using (var ctx = _db.GetDbContext())
        {
            await WaifuTestHelper.CreateWaifu(ctx, 1001);
            await WaifuTestHelper.CreateWaifu(ctx, 1002);
            await WaifuTestHelper.CreateFan(ctx, 2001, 1001);
        }

        var result = await _svc.BecomeFanAsync(2001, 1002);
        Assert.That(result.IsT3, Is.True, "Expected Success");

        await using (var ctx = _db.GetDbContext())
        {
            var allFans = await ctx.GetTable<WaifuFan>().Where(x => x.UserId == 2001).ToListAsyncLinqToDB();
            Assert.That(allFans.Count, Is.EqualTo(1));
            Assert.That(allFans[0].WaifuUserId, Is.EqualTo(1002));
        }
    }

    [Test]
    public async Task BecomeFan_ManagerCanSwitch_Success()
    {
        await using (var ctx = _db.GetDbContext())
        {
            await WaifuTestHelper.CreateWaifu(ctx, 1001, managerId: 2001);
            await WaifuTestHelper.CreateWaifu(ctx, 1002);
            await WaifuTestHelper.CreateFan(ctx, 2001, 1001);
        }

        var result = await _svc.BecomeFanAsync(2001, 1002);
        Assert.That(result.IsT3, Is.True, "Expected Success");

        await using (var ctx = _db.GetDbContext())
        {
            var fan = await ctx.GetTable<WaifuFan>()
                .FirstOrDefaultAsyncLinqToDB(x => x.UserId == 2001);
            Assert.That(fan, Is.Not.Null);
            Assert.That(fan!.WaifuUserId, Is.EqualTo(1002));

            // Manager status should remain on the old waifu
            var wi = await ctx.GetTable<WaifuInfo>().FirstAsyncLinqToDB(x => x.UserId == 1001);
            Assert.That(wi.ManagerUserId, Is.EqualTo(2001));
        }
    }

    [Test]
    public async Task StopBeingFan_Success_DeletesRow()
    {
        await using (var ctx = _db.GetDbContext())
        {
            await WaifuTestHelper.CreateWaifu(ctx, 1001);
            await WaifuTestHelper.CreateFan(ctx, 2001, 1001);
        }

        var result = await _svc.StopBeingFanAsync(2001);
        Assert.That(result.IsT1, Is.True, "Expected Success");

        await using (var ctx = _db.GetDbContext())
        {
            var fan = await ctx.GetTable<WaifuFan>().FirstOrDefaultAsyncLinqToDB(x => x.UserId == 2001);
            Assert.That(fan, Is.Null);
        }
    }

    [Test]
    public async Task StopBeingFan_NotBacking_ReturnsError()
    {
        var result = await _svc.StopBeingFanAsync(9999);
        Assert.That(result.IsT0, Is.True, "Expected ErrNotBacking");
    }
}
