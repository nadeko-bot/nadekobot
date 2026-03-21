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
public class WaifuGiftTests
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

    private static readonly System.Collections.Generic.IReadOnlyList<WaifuGiftItem> _allItems =
        WaifuGiftItems.FromConfig(new WaifuConfig().Items);

    private static WaifuGiftItem GetTodaysFoodItem()
        => WaifuGiftItems.GetTodaysItems(_allItems).First(x => x.Type == GiftItemType.Food);

    private static WaifuGiftItem GetTodaysMoodItem()
        => WaifuGiftItems.GetTodaysItems(_allItems).First(x => x.Type == GiftItemType.Mood);

    [Test]
    public async Task Gift_FoodAndMood_CorrectStatIncrease()
    {
        var food = GetTodaysFoodItem();
        var mood = GetTodaysMoodItem();

        await using (var ctx = _db.GetDbContext())
        {
            await WaifuTestHelper.CreateWaifu(ctx, 1001, food: 100, mood: 200);
        }

        await _svc.GiftAsync(2001, 1001, food.Name, 2);
        await _svc.GiftAsync(2001, 1001, mood.Name, 3);

        await using (var ctx = _db.GetDbContext())
        {
            var wi = await ctx.GetTable<WaifuInfo>().FirstAsyncLinqToDB(x => x.UserId == 1001);
            Assert.That(wi.Food, Is.EqualTo(100 + food.Effect * 2));
            Assert.That(wi.Mood, Is.EqualTo(200 + mood.Effect * 3));
        }
    }

    [Test]
    public async Task Gift_StatsCappedAt1000()
    {
        var food = GetTodaysFoodItem();
        await using (var ctx = _db.GetDbContext())
            await WaifuTestHelper.CreateWaifu(ctx, 1001, food: 990);

        await _svc.GiftAsync(2001, 1001, food.Name, 100);

        await using (var ctx = _db.GetDbContext())
        {
            var wi = await ctx.GetTable<WaifuInfo>().FirstAsyncLinqToDB(x => x.UserId == 1001);
            Assert.That(wi.Food, Is.EqualTo(1000));
        }
    }

    [Test]
    public async Task Gift_InsufficientFunds_ReturnsError()
    {
        _cs.RemoveAsync(Arg.Any<ulong>(), Arg.Any<long>(), Arg.Any<TxData?>()).Returns(false);

        await using (var ctx = _db.GetDbContext())
            await WaifuTestHelper.CreateWaifu(ctx, 1001);

        var result = await _svc.GiftAsync(2001, 1001, GetTodaysFoodItem().Name);
        Assert.That(result.IsT1, Is.True, "Expected ErrInsufficientFunds");
    }

    [Test]
    public async Task Gift_ItemNotAvailable_ReturnsError()
    {
        await using (var ctx = _db.GetDbContext())
            await WaifuTestHelper.CreateWaifu(ctx, 1001);

        var result = await _svc.GiftAsync(2001, 1001, "NonExistentItemXYZ");
        Assert.That(result.IsT3, Is.True, "Expected ErrItemNotAvailable");
    }

    [Test]
    public async Task Gift_PersistsGiftCount_IncrementOnRepeat()
    {
        var food = GetTodaysFoodItem();
        var mood = GetTodaysMoodItem();

        await using (var ctx = _db.GetDbContext())
            await WaifuTestHelper.CreateWaifu(ctx, 1001, food: 100, mood: 100);

        await _svc.GiftAsync(2001, 1001, food.Name, 3);
        await _svc.GiftAsync(2001, 1001, food.Name, 2);
        await _svc.GiftAsync(2001, 1001, mood.Name, 1);

        var gifts = await _svc.GetGiftCountsAsync(1001);
        Assert.That(gifts, Has.Count.EqualTo(2));

        var foodGift = gifts.First(g => g.Item.Id == food.Id);
        Assert.That(foodGift.Count, Is.EqualTo(5));

        var moodGift = gifts.First(g => g.Item.Id == mood.Id);
        Assert.That(moodGift.Count, Is.EqualTo(1));
    }
}
