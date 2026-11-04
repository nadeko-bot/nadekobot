using System.Threading;
using System.Threading.Tasks;
using NadekoBot.Modules.Xp.Services;
using NUnit.Framework;

namespace NadekoBot.Tests.Xp;

public class XpCooldownMapTests
{
    private XpCooldownMap _map = null!;

    [SetUp]
    public void SetUp()
        => _map = new XpCooldownMap();

    [Test]
    public void Cooldown_AllowsOneCallPerMemberUntilItEnds_AndZeroAlwaysAllows()
    {
        Assert.That(_map.TryAddCooldown(1, 0f), Is.True);
        Assert.That(_map.TryAddCooldown(1, 0f), Is.True);

        Assert.That(_map.TryAddCooldown(2, 5f), Is.True);
        Assert.That(_map.TryAddCooldown(2, 5f), Is.False);
        Assert.That(_map.TryAddCooldown(3, 5f), Is.True);

        var successCount = 0;
        Parallel.For(0, 100, _ =>
        {
            if (_map.TryAddCooldown(42, 5f))
                Interlocked.Increment(ref successCount);
        });

        Assert.That(successCount, Is.EqualTo(1));
    }
}
