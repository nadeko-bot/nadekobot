#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NadekoBot.Modules.Music;
using NUnit.Framework;

namespace NadekoBot.Tests;

// Tests OncePerKey, which MusicService.GetOrCreateMusicPlayerAsync uses, because each new player starts a play loop
// which never ends.
[TestFixture]
public sealed class MusicPlayerReuseTests
{
    private const ulong GUILD = 1;
    private const ulong OTHER_GUILD = 2;
    private const int CALLERS = 10;

    [Test]
    public async Task GetOrCreate_BuildsOnePlayerForEachServerAlsoWhenManyCallersAskAtOnce()
    {
        var players = new NonBlocking.ConcurrentDictionary<ulong, object>();
        var locks = new NonBlocking.ConcurrentDictionary<ulong, SemaphoreSlim>();
        var builds = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<object> BuildAsync(ulong _)
        {
            Interlocked.Increment(ref builds);
            await release.Task;
            return new object();
        }

        var burst = Enumerable.Range(0, CALLERS)
            .Select(_ => Task.Run(() => OncePerKey.GetOrCreateAsync(players, locks, GUILD, GUILD, BuildAsync)))
            .ToArray();
        await Task.Delay(50);
        release.SetResult();
        var results = await Task.WhenAll(burst);

        Assert.That(builds, Is.EqualTo(1));
        Assert.That(results.Select(static r => r.Value).Distinct().Count(), Is.EqualTo(1));
        Assert.That(results.Count(static r => r.Created), Is.EqualTo(1));

        var again = await OncePerKey.GetOrCreateAsync(players, locks, GUILD, GUILD, BuildAsync);
        Assert.That((again.Value, again.Created, builds), Is.EqualTo((results[0].Value, false, 1)));

        var other = await OncePerKey.GetOrCreateAsync(players, locks, OTHER_GUILD, OTHER_GUILD, BuildAsync);
        Assert.That(other.Value, Is.Not.SameAs(results[0].Value));

        // After .destroy removes the player, the next music command gets a new one.
        players.TryRemove(GUILD, out _);
        var fresh = await OncePerKey.GetOrCreateAsync(players, locks, GUILD, GUILD, BuildAsync);
        Assert.That((fresh.Created, builds), Is.EqualTo((true, 3)));
    }
}
