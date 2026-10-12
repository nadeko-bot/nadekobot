#nullable enable
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
public class WaifuOptInOutTests
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
        var time = new FakeTimeProvider(new(2025, 1, 7, 0, 0, 0, System.TimeSpan.Zero));
        var client = Substitute.For<DiscordSocketClient>();
        _svc = new WaifuService(_db, cache, _cs, client, WaifuTestHelper.CreateConfigService(), WaifuTestHelper.CreatePatronageService(), null!, null!, null!, time);
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    [Test]
    public async Task OptIn_ChargesFeeOnce_CreatesWaifuWithDefaults()
    {
        _cs.RemoveAsync(Arg.Any<ulong>(), Arg.Any<long>(), Arg.Any<TxData?>()).Returns(true);

        var result = await _svc.OptInAsync(1001);
        Assert.That(result.IsT2, Is.True, "Expected Success");

        await using (var ctx = _db.GetDbContext())
        {
            var wi = await ctx.GetTable<WaifuInfo>().FirstOrDefaultAsyncLinqToDB(x => x.UserId == 1001);
            Assert.That(wi, Is.Not.Null);
            Assert.That(wi!.Mood, Is.EqualTo(500));
            Assert.That(wi.Food, Is.EqualTo(500));
            Assert.That(wi.WaifuFeePercent, Is.EqualTo(5));
            Assert.That(wi.Price, Is.EqualTo(1000));
            Assert.That(wi.IsHubby, Is.False);
        }

        await _cs.Received(1).RemoveAsync(1001, 10_000, Arg.Any<TxData?>());
        _cs.ClearReceivedCalls();

        var again = await _svc.OptInAsync(1001);
        Assert.That(again.IsT0, Is.True, "Expected ErrAlreadyOptedIn");
        await _cs.DidNotReceive().RemoveAsync(Arg.Any<ulong>(), Arg.Any<long>(), Arg.Any<TxData?>());

        _cs.RemoveAsync(Arg.Any<ulong>(), Arg.Any<long>(), Arg.Any<TxData?>()).Returns(false);

        var broke = await _svc.OptInAsync(1002);
        Assert.That(broke.IsT1, Is.True, "Expected ErrInsufficientFunds");

        await using (var ctx = _db.GetDbContext())
        {
            var wi = await ctx.GetTable<WaifuInfo>().FirstOrDefaultAsyncLinqToDB(x => x.UserId == 1002);
            Assert.That(wi, Is.Null);
        }
    }

    [Test]
    public async Task OptOut_DeletesWaifuOnlyWhenOptedInWithoutFans()
    {
        await using (var ctx = _db.GetDbContext())
        {
            await WaifuTestHelper.CreateWaifu(ctx, 1001);
            await WaifuTestHelper.CreateWaifu(ctx, 1002);
            await WaifuTestHelper.CreateFan(ctx, 2001, 1002);
        }

        var notOptedIn = await _svc.OptOutAsync(9999);
        Assert.That(notOptedIn.IsT0, Is.True, "Expected ErrNotOptedIn");

        var hasFans = await _svc.OptOutAsync(1002);
        Assert.That(hasFans.IsT1, Is.True, "Expected ErrHasFans");

        var result = await _svc.OptOutAsync(1001);
        Assert.That(result.IsT2, Is.True, "Expected Success");

        await using (var ctx = _db.GetDbContext())
        {
            var wi = await ctx.GetTable<WaifuInfo>().FirstOrDefaultAsyncLinqToDB(x => x.UserId == 1001);
            Assert.That(wi, Is.Null);
        }
    }
}
