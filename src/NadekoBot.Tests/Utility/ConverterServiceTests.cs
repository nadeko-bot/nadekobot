using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Discord.WebSocket;
using Nadeko.Common;
using NadekoBot.Common;
using NadekoBot.Modules.Utility.Common;
using NadekoBot.Modules.Utility.Services;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Utility;

public class ConverterServiceTests
{
    [Test]
    public async Task FixedUnitsWork_WithoutCurrencyRates()
    {
        var svc = new ConverterService(Substitute.For<DiscordSocketClient>(),
            new MemoryBotCache(),
            Substitute.For<IHttpClientFactory>());
        await svc.LoadStaticUnitsAsync();

        var km = await svc.ConvertAsync("M", "km", 1500);
        Assert.That(km.Status, Is.EqualTo(ConvertStatus.Ok));
        Assert.That(km.Value, Is.EqualTo(1.5m));

        var temp = await svc.ConvertAsync("c", "F", 100);
        Assert.That(temp.Value, Is.EqualTo(212m));

        Assert.That((await svc.ConvertAsync("m", "kg", 1)).Status, Is.EqualTo(ConvertStatus.TypeMismatch));
        Assert.That((await svc.ConvertAsync("usd", "eur", 1)).Status, Is.EqualTo(ConvertStatus.NotFound));
    }

    [Test]
    public async Task CurrencyRatesComeFromCache()
    {
        var cache = new MemoryBotCache();
        await cache.AddAsync(new TypedKey<List<ConvertUnit>>("convert:currency"),
            new List<ConvertUnit>
            {
                new() { Triggers = ["EUR"], Modifier = 1m, UnitType = "currency" },
                new() { Triggers = ["USD"], Modifier = 1.25m, UnitType = "currency" }
            });

        var svc = new ConverterService(Substitute.For<DiscordSocketClient>(),
            cache,
            Substitute.For<IHttpClientFactory>());
        await svc.LoadStaticUnitsAsync();

        var res = await svc.ConvertAsync("eur", "usd", 10);
        Assert.That(res.Status, Is.EqualTo(ConvertStatus.Ok));
        Assert.That(res.Value, Is.EqualTo(12.5m));
    }
}
