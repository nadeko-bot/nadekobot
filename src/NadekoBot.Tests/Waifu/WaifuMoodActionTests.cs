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
using OneOf.Types;

namespace NadekoBot.Tests.Waifu;

[TestFixture]
public class WaifuMoodActionTests
{
    private WaifuService _svc = null!;
    private IBotCache _cache = null!;
    private TestDbService _db = null!;

    [SetUp]
    public void Setup()
    {
        _db = new TestDbService();
        var cs = Substitute.For<ICurrencyService>();
        _cache = Substitute.For<IBotCache>();
        var time = new FakeTimeProvider(new(2025, 1, 7, 0, 0, 0, TimeSpan.Zero));
        var client = Substitute.For<DiscordSocketClient>();
        _svc = new WaifuService(_db, _cache, cs, client, WaifuTestHelper.CreateConfigService(), WaifuTestHelper.CreatePatronageService(), null!, null!, null!, time);
        _cache.GetAsync(Arg.Any<TypedKey<int>>())
            .Returns(new System.Threading.Tasks.ValueTask<OneOf.OneOf<int, None>>(new None()));
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    [Test]
    public async Task ImproveMood_IncreasesByActionAndCapsAt1000()
    {
        // Hug=50, Kiss=75, Pat=37
        await using (var ctx = _db.GetDbContext())
        {
            await WaifuTestHelper.CreateWaifu(ctx, 1001, mood: 0);
            await WaifuTestHelper.CreateWaifu(ctx, 1002, mood: 0);
            await WaifuTestHelper.CreateWaifu(ctx, 1003, mood: 0);
            await WaifuTestHelper.CreateWaifu(ctx, 1004, mood: 980);
        }

        await _svc.ImproveMoodAsync(2001, 1001, WaifuAction.Hug);
        await _svc.ImproveMoodAsync(2002, 1002, WaifuAction.Kiss);
        await _svc.ImproveMoodAsync(2003, 1003, WaifuAction.Pat);
        await _svc.ImproveMoodAsync(2004, 1004, WaifuAction.Hug);

        await using (var ctx = _db.GetDbContext())
        {
            Assert.That((await ctx.GetTable<WaifuInfo>().FirstAsyncLinqToDB(x => x.UserId == 1001)).Mood, Is.EqualTo(50));
            Assert.That((await ctx.GetTable<WaifuInfo>().FirstAsyncLinqToDB(x => x.UserId == 1002)).Mood, Is.EqualTo(75));
            Assert.That((await ctx.GetTable<WaifuInfo>().FirstAsyncLinqToDB(x => x.UserId == 1003)).Mood, Is.EqualTo(37));
            Assert.That((await ctx.GetTable<WaifuInfo>().FirstAsyncLinqToDB(x => x.UserId == 1004)).Mood, Is.EqualTo(1000));
        }
    }

    [Test]
    public async Task ImproveMood_NonWaifuOrNoActionsLeft_Fails()
    {
        var nonWaifu = await _svc.ImproveMoodAsync(2001, 1009, WaifuAction.Hug);
        Assert.That(nonWaifu.IsT3, Is.True, "Expected NoEffect");

        _cache.GetAsync(Arg.Any<TypedKey<int>>())
            .Returns(new System.Threading.Tasks.ValueTask<OneOf.OneOf<int, None>>((OneOf.OneOf<int, None>)2));

        await using (var ctx = _db.GetDbContext())
            await WaifuTestHelper.CreateWaifu(ctx, 1001, mood: 500);

        var noActions = await _svc.ImproveMoodAsync(2001, 1001, WaifuAction.Hug);
        Assert.That(noActions.IsT1, Is.True, "Expected ErrNoActionsLeft");
    }

    [Test]
    public async Task ImproveFood_IncreasesFoodCapsAt1000AndRejectsSelf()
    {
        await using (var ctx = _db.GetDbContext())
        {
            await WaifuTestHelper.CreateWaifu(ctx, 1001, food: 0);
            await WaifuTestHelper.CreateWaifu(ctx, 1002, food: 980);
        }

        var result = await _svc.ImproveFoodAsync(2001, 1001);
        Assert.That(result.IsT4, Is.True, "Expected Success");
        Assert.That(result.AsT4.Value, Is.EqualTo(50));

        await _svc.ImproveFoodAsync(2002, 1002);

        var self = await _svc.ImproveFoodAsync(1009, 1009);
        Assert.That(self.IsT0, Is.True, "Expected ErrSelfNotAllowed");

        await using (var ctx = _db.GetDbContext())
        {
            Assert.That((await ctx.GetTable<WaifuInfo>().FirstAsyncLinqToDB(x => x.UserId == 1001)).Food, Is.EqualTo(50));
            Assert.That((await ctx.GetTable<WaifuInfo>().FirstAsyncLinqToDB(x => x.UserId == 1002)).Food, Is.EqualTo(1000));
        }
    }
}
