using System.Collections.Generic;
using System.Threading.Tasks;
using NadekoBot.Services;
using NUnit.Framework;

namespace NadekoBot.Tests.Administration;

public class GreetQueueTests
{
    private const ulong RAID_GUILD = 1;
    private const ulong OTHER_GUILD = 2;

    [Test]
    public async Task Raid_IsCappedAndDoesNotDelayOtherServers()
    {
        var queue = new GreetQueue<int>();

        var accepted = 0;
        var firstDrops = 0;
        for (var i = 0; i < GreetQueue<int>.MAX_PENDING_PER_GUILD * 3; i++)
        {
            if (queue.TryEnqueue(RAID_GUILD, i, out var firstDrop))
                accepted++;
            else if (firstDrop)
                firstDrops++;
        }

        Assert.That(accepted, Is.EqualTo(GreetQueue<int>.MAX_PENDING_PER_GUILD));
        Assert.That(firstDrops, Is.EqualTo(1));

        Assert.That(queue.TryEnqueue(OTHER_GUILD, 100, out _), Is.True);
        Assert.That(queue.TryEnqueue(OTHER_GUILD, 101, out _), Is.True);

        var order = new List<(ulong, int)>();
        for (var i = 0; i < accepted + 2; i++)
            order.Add(await queue.DequeueAsync());

        Assert.That(order[..4],
            Is.EqualTo(new[] { (RAID_GUILD, 0), (OTHER_GUILD, 100), (RAID_GUILD, 1), (OTHER_GUILD, 101) }));
        Assert.That(order[^1], Is.EqualTo((RAID_GUILD, accepted - 1)));

        // an emptied server can queue again, and its next raid is reported again
        Assert.That(queue.TryEnqueue(RAID_GUILD, 7, out _), Is.True);
        Assert.That(await queue.DequeueAsync(), Is.EqualTo((RAID_GUILD, 7)));

        for (var i = 0; i < GreetQueue<int>.MAX_PENDING_PER_GUILD; i++)
            queue.TryEnqueue(RAID_GUILD, i, out _);
        Assert.That(queue.TryEnqueue(RAID_GUILD, 0, out var again), Is.False);
        Assert.That(again, Is.True);
    }
}
