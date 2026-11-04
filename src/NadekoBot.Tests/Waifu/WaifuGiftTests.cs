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
        _svc = new WaifuService(_db, cache, _cs, client, WaifuTestHelper.CreateConfigService(), WaifuTestHelper.CreatePatronageService(), null!, null!, null!, time);
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
    public async Task Gift_RaisesStatsCappedAt1000CountsRepeatsAndRefusesUnknownItemsOrNoMoney()
    {
        // each day offers 3 food and 3 mood items, one from each price tier
        var today = WaifuGiftItems.GetTodaysItems(_allItems);
        Assert.That(today.Count(x => x.Type == GiftItemType.Food), Is.EqualTo(3));
        Assert.That(today.Select(x => x.Price).OrderBy(x => x), Is.EqualTo(new long[] { 10, 50, 250, 1000, 2500, 5000 }));
        Assert.That(WaifuGiftItems.GetTodaysItems(_allItems).Select(x => x.Id), Is.EqualTo(today.Select(x => x.Id)));
        Assert.That(WaifuGiftItems.FindTodaysItem(_allItems, today[0].Name.ToUpperInvariant())?.Id, Is.EqualTo(today[0].Id));
        Assert.That(_allItems.Count(x => x.Price > 0), Is.EqualTo(24));

        var food = GetTodaysFoodItem();
        var mood = GetTodaysMoodItem();

        await using (var ctx = _db.GetDbContext())
        {
            await WaifuTestHelper.CreateWaifu(ctx, 1001, food: 100, mood: 200);
            await WaifuTestHelper.CreateWaifu(ctx, 1002, food: 990);
        }

        await _svc.GiftAsync(2001, 1001, food.Name, 1);
        await _svc.GiftAsync(2001, 1001, food.Name, 1);
        await _svc.GiftAsync(2001, 1001, mood.Name, 3);
        await _svc.GiftAsync(2001, 1002, food.Name, 100);

        await using (var ctx = _db.GetDbContext())
        {
            var wi = await ctx.GetTable<WaifuInfo>().FirstAsyncLinqToDB(x => x.UserId == 1001);
            Assert.That(wi.Food, Is.EqualTo(100 + food.Effect * 2));
            Assert.That(wi.Mood, Is.EqualTo(200 + mood.Effect * 3));

            var capped = await ctx.GetTable<WaifuInfo>().FirstAsyncLinqToDB(x => x.UserId == 1002);
            Assert.That(capped.Food, Is.EqualTo(1000));
        }

        var gifts = await _svc.GetGiftCountsAsync(1001);
        Assert.That(gifts, Has.Count.EqualTo(2));
        Assert.That(gifts.First(g => g.Item.Id == food.Id).Count, Is.EqualTo(2));
        Assert.That(gifts.First(g => g.Item.Id == mood.Id).Count, Is.EqualTo(3));

        var unknown = await _svc.GiftAsync(2001, 1001, "NonExistentItemXYZ");
        Assert.That(unknown.IsT3, Is.True, "Expected ErrItemNotAvailable");

        _cs.RemoveAsync(Arg.Any<ulong>(), Arg.Any<long>(), Arg.Any<TxData?>()).Returns(false);

        var broke = await _svc.GiftAsync(2001, 1001, food.Name);
        Assert.That(broke.IsT1, Is.True, "Expected ErrInsufficientFunds");
    }
}
