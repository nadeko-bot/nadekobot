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
public class WaifuFeeTests
{
    private WaifuService _svc = null!;
    private TestDbService _db = null!;

    [SetUp]
    public void Setup()
    {
        _db = new TestDbService();
        var cs = Substitute.For<ICurrencyService>();
        var cache = Substitute.For<IBotCache>();
        var time = new FakeTimeProvider(new(2025, 1, 7, 0, 0, 0, TimeSpan.Zero));
        var client = Substitute.For<DiscordSocketClient>();
        _svc = new WaifuService(_db, cache, cs, client, WaifuTestHelper.CreateConfigService(), WaifuTestHelper.CreatePatronageService(), null!, time);
    }

    [TearDown]
    public void TearDown() => _db.Dispose();

    [Test]
    public async Task SetFee_ValidPercent_UpdatesSuccessfully()
    {
        await using (var ctx = _db.GetDbContext())
            await WaifuTestHelper.CreateWaifu(ctx, 1001, fee: 5);

        var result = await _svc.SetWaifuFeeAsync(1001, 3);
        Assert.That(result.IsT3, Is.True, "Expected Success");

        await using (var ctx = _db.GetDbContext())
        {
            var wi = await ctx.GetTable<WaifuInfo>().FirstAsyncLinqToDB(x => x.UserId == 1001);
            Assert.That(wi.WaifuFeePercent, Is.EqualTo(3));
        }
    }

    [Test]
    public async Task SetFee_OutOfRange_ReturnsInvalidPercent()
    {
        await using (var ctx = _db.GetDbContext())
            await WaifuTestHelper.CreateWaifu(ctx, 1001);

        Assert.That((await _svc.SetWaifuFeeAsync(1001, 0)).IsT2, Is.True);
        Assert.That((await _svc.SetWaifuFeeAsync(1001, 6)).IsT2, Is.True);
    }

    [Test]
    public async Task SetFee_NotOptedIn_ReturnsError()
    {
        var result = await _svc.SetWaifuFeeAsync(9999, 3);
        Assert.That(result.IsT1, Is.True, "Expected ErrNotOptedIn");
    }
}
