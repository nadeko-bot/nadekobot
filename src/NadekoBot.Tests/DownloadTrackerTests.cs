#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Discord.WebSocket;
using Microsoft.Extensions.Time.Testing;
using NadekoBot.Common;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests;

[TestFixture]
public sealed class DownloadTrackerTests
{
    private const ulong GUILD = 101;
    private const int SMALL = 500;

    private sealed class FakeGuild
    {
        public bool HasAll;
        public int Downloads;
        public Task Pending = Task.CompletedTask;
    }

    private static DownloadTracker NewTracker(FakeTimeProvider time, IntentState? intents = null)
        => new(intents ?? IntentState.All, Substitute.For<DiscordSocketClient>(), time);

    private static Task<bool> EnsureInternal(DownloadTracker tracker, FakeGuild g, ulong id = GUILD, int members = SMALL, bool force = false)
        => tracker.EnsureAsync(id, members, g, static s => s.HasAll, static s =>
        {
            s.Downloads++;
            return s.Pending;
        }, force);

    [Test]
    public async Task Ensure_SharesOneDownloadAndSkipsLargeServersAndMissingIntent()
    {
        var tracker = NewTracker(new FakeTimeProvider());
        var gate = new TaskCompletionSource();
        var guild = new FakeGuild { Pending = gate.Task };

        var calls = Enumerable.Range(0, 10).Select(_ => EnsureInternal(tracker, guild)).ToArray();
        guild.HasAll = true;
        gate.SetResult();

        Assert.That(await Task.WhenAll(calls), Is.All.True);
        Assert.That(guild.Downloads, Is.EqualTo(1));

        var large = new FakeGuild();
        Assert.That(await EnsureInternal(tracker, large, 202, DownloadTracker.MAX_MEMBERS + 1), Is.False);
        Assert.That(large.Downloads, Is.Zero);
        await EnsureInternal(tracker, large, 202, DownloadTracker.MAX_MEMBERS + 1, force: true);
        Assert.That(large.Downloads, Is.EqualTo(1));

        var noIntent = NewTracker(new FakeTimeProvider(), IntentState.None);
        var other = new FakeGuild();
        Assert.That(await EnsureInternal(noIntent, other, force: true), Is.False);
        Assert.That(other.Downloads, Is.Zero);
    }

    [Test]
    public async Task Ensure_StopsAStuckDownloadAtTheTimeoutAndWaitsBeforeTheNextRequest()
    {
        var time = new FakeTimeProvider();
        var tracker = NewTracker(time);
        var guild = new FakeGuild { Pending = new TaskCompletionSource().Task };

        var stuck = EnsureInternal(tracker, guild);
        Assert.That(stuck.IsCompleted, Is.False);
        time.Advance(DownloadTracker.Timeout);
        Assert.That(await stuck, Is.False);

        await EnsureInternal(tracker, guild);
        Assert.That(guild.Downloads, Is.EqualTo(1));

        // The owner may ask again once Discord's 30 seconds for the server are over, but not sooner.
        guild.Pending = Task.CompletedTask;
        await EnsureInternal(tracker, guild, force: true);
        await EnsureInternal(tracker, guild, force: true);
        Assert.That(guild.Downloads, Is.EqualTo(2));

        time.Advance(DownloadTracker.Cooldown);
        await EnsureInternal(tracker, guild);
        Assert.That(guild.Downloads, Is.EqualTo(3));
    }

    [Test]
    public async Task Ensure_KeepsTheShardWithinItsRequestRateAndMemberBudget()
    {
        var time = new FakeTimeProvider();
        var tracker = NewTracker(time);
        var full = DownloadTracker.MAX_REQUESTS_PER_MINUTE;

        for (var id = 1; id <= full; id++)
            await EnsureInternal(tracker, new FakeGuild(), (ulong)id, DownloadTracker.MAX_MEMBERS);

        var small = new FakeGuild();
        Assert.That(await EnsureInternal(tracker, small, 1000), Is.False);
        Assert.That(small.Downloads, Is.Zero);

        time.Advance(TimeSpan.FromMinutes(1));
        await EnsureInternal(tracker, small, 1000);
        Assert.That(small.Downloads, Is.EqualTo(1));

        var room = (DownloadTracker.MAX_SHARD_MEMBERS - (full * DownloadTracker.MAX_MEMBERS) - SMALL) / DownloadTracker.MAX_MEMBERS;
        for (var id = 1; id <= room; id++)
            await EnsureInternal(tracker, new FakeGuild(), (ulong)(full + id), DownloadTracker.MAX_MEMBERS);

        var over = new FakeGuild();
        Assert.That(await EnsureInternal(tracker, over, 2000, DownloadTracker.MAX_MEMBERS), Is.False);
        Assert.That(over.Downloads, Is.Zero);

        // The member lists which no command used for a while leave the cache and give the budget back.
        time.Advance(DownloadTracker.IdleTime);
        var purged = 0;
        Assert.That(tracker.EvictIdle(_ => purged++), Is.EqualTo(full + 1 + room));
        Assert.That(purged, Is.EqualTo(full + 1 + room));

        await EnsureInternal(tracker, over, 2000, DownloadTracker.MAX_MEMBERS);
        Assert.That(over.Downloads, Is.EqualTo(1));
    }
}
