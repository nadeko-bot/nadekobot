using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NadekoBot.Modules.Xp;
using NadekoBot.Tests.Waifu;
using NUnit.Framework;

namespace NadekoBot.Tests;

public class ReputationServiceTests
{
    private const ulong GUILD_ID = 1;
    private const ulong OTHER_GUILD_ID = 2;
    private const ulong GIVER = 10;
    private const ulong GIVER_2 = 11;
    private const ulong ALICE = 20;
    private const ulong BOB = 21;

    private TestDbService _db = null!;
    private FakeTimeProvider _time = null!;
    private ReputationService _svc = null!;

    [SetUp]
    public void Setup()
    {
        _db = new();
        _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        _svc = new(_db, _time);
    }

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    [Test]
    public async Task Give_AllowsOnePointPerCooldownPerServer_AndLeaderboardSkipsZero()
    {
        var first = await _svc.GiveAsync(GUILD_ID, GIVER, ALICE);
        Assert.That(first.Given, Is.True);
        Assert.That(first.Reputation, Is.EqualTo(1));

        var again = await _svc.GiveAsync(GUILD_ID, GIVER, BOB);
        Assert.That(again.Given, Is.False);
        Assert.That(again.NextGiveAt, Is.EqualTo(_time.GetUtcNow().UtcDateTime + ReputationService.GiveCooldown));

        var otherServer = await _svc.GiveAsync(OTHER_GUILD_ID, GIVER, BOB);
        Assert.That(otherServer.Given, Is.True);

        var second = await _svc.GiveAsync(GUILD_ID, GIVER_2, ALICE);
        Assert.That(second.Reputation, Is.EqualTo(2));

        _time.Advance(ReputationService.GiveCooldown);
        var afterCooldown = await _svc.GiveAsync(GUILD_ID, GIVER, ALICE);
        Assert.That(afterCooldown.Reputation, Is.EqualTo(3));

        var alice = await _svc.GetAsync(GUILD_ID, ALICE);
        var giver = await _svc.GetAsync(GUILD_ID, GIVER);
        Assert.That(alice, Is.EqualTo(new RepInfo(3, null)));
        Assert.That(giver.Reputation, Is.Zero);
        Assert.That(giver.NextGiveAt, Is.Not.Null);

        await _svc.GiveAsync(GUILD_ID, GIVER_2, BOB);

        var lb = await _svc.GetLeaderboardAsync(GUILD_ID, 0);
        Assert.That(lb.Select(x => (x.UserId, x.Reputation)),
            Is.EqualTo(new[] { (ALICE, 3), (BOB, 1) }));
        Assert.That(await _svc.GetLeaderboardAsync(GUILD_ID, 1), Is.Empty);
    }
}
