using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Nadeko.Common;
using NadekoBot.Modules.Gambling.Services;
using NUnit.Framework;

namespace NadekoBot.Tests.Gambling;

public class TimelyClaimStoreTests
{
    private const int PERIOD_HOURS = 12;

    [Test]
    public async Task OneClaimPerPeriodPerMember()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new TimelyClaimStore(new MemoryBotCache(), time);

        var results = await Task.WhenAll(
            store.TryClaimAsync(1, PERIOD_HOURS),
            store.TryClaimAsync(1, PERIOD_HOURS),
            store.TryClaimAsync(1, PERIOD_HOURS));
        Assert.That(Array.FindAll(results, r => r is null), Has.Length.EqualTo(1));

        Assert.That(await store.TryClaimAsync(2, PERIOD_HOURS), Is.Null);

        time.Advance(TimeSpan.FromHours(2));
        Assert.That(await store.TryClaimAsync(1, PERIOD_HOURS), Is.EqualTo(TimeSpan.FromHours(PERIOD_HOURS - 2)));
    }

    [Test]
    public async Task ResetAllowsANewClaim()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new TimelyClaimStore(new MemoryBotCache(), time);

        Assert.That(await store.TryClaimAsync(1, PERIOD_HOURS), Is.Null);
        Assert.That(await store.TryClaimAsync(1, PERIOD_HOURS), Is.Not.Null);

        time.Advance(TimeSpan.FromSeconds(1));
        await store.ResetAllAsync();

        Assert.That(await store.TryClaimAsync(1, PERIOD_HOURS), Is.Null);
        Assert.That(await store.TryClaimAsync(1, PERIOD_HOURS), Is.Not.Null);
    }
}
