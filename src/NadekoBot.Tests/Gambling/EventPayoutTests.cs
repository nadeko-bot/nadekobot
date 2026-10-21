using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NadekoBot.Modules.Gambling.Common.Events;
using NadekoBot.Services;
using NadekoBot.Services.Currency;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Gambling;

public class EventPayoutTests
{
    private const long AMOUNT = 100;

    private ICurrencyService _cs = null!;
    private List<ulong> _paid = null!;

    [SetUp]
    public void Setup()
    {
        _paid = new();
        _cs = Substitute.For<ICurrencyService>();
        _cs.AddBulkAsync(Arg.Any<IReadOnlyCollection<ulong>>(), AMOUNT, Arg.Any<TxData>())
            .Returns(Task.CompletedTask)
            .AndDoes(ci => _paid.AddRange(ci.Arg<IReadOnlyCollection<ulong>>()));
    }

    [Test]
    public async Task PotLimitsAndDeduplicatesClaims()
    {
        var payout = new EventPayout(_cs, AMOUNT, AMOUNT * 2 + 50, new("event", "test"));

        Assert.That(payout.TryClaim(1), Is.True);
        Assert.That(payout.TryClaim(1), Is.False);
        Assert.That(payout.PotEmptied, Is.False);
        Assert.That(payout.TryClaim(2), Is.True);
        Assert.That(payout.PotEmptied, Is.True);
        Assert.That(payout.TryClaim(3), Is.False);
        Assert.That(payout.Pot, Is.EqualTo(50));

        Assert.That(await payout.PayQueuedAsync(), Is.EqualTo(2));
        Assert.That(_paid, Is.EquivalentTo(new ulong[] { 1, 2 }));
    }

    [Test]
    public async Task StopPaysMembersWhoClaimedBeforeIt()
    {
        var payout = new EventPayout(_cs, AMOUNT, 0, new("event", "test"));
        using var cts = new CancellationTokenSource();
        var stopped = 0;

        var run = payout.RunAsync(TimeSpan.FromHours(1),
            _ => Task.CompletedTask,
            () =>
            {
                Interlocked.Increment(ref stopped);
                return Task.CompletedTask;
            },
            cts.Token);

        payout.TryClaim(7);
        payout.TryClaim(8);
        cts.Cancel();
        await run;

        Assert.That(stopped, Is.EqualTo(1));
        Assert.That(_paid.OrderBy(x => x), Is.EqualTo(new ulong[] { 7, 8 }));
    }
}
