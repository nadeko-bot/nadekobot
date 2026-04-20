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
    public void ZeroCooldown_AlwaysReturnsTrue()
    {
        Assert.That(_map.TryAddCooldown(1, 0f), Is.True);
        Assert.That(_map.TryAddCooldown(1, 0f), Is.True);
        Assert.That(_map.TryAddCooldown(1, 0f), Is.True);
    }

    [Test]
    public void SecondCallWithinCooldown_ReturnsFalse()
    {
        Assert.That(_map.TryAddCooldown(1, 5f), Is.True);
        Assert.That(_map.TryAddCooldown(1, 5f), Is.False);
    }

    [Test]
    public void DifferentUsers_BothSucceed()
    {
        Assert.That(_map.TryAddCooldown(1, 5f), Is.True);
        Assert.That(_map.TryAddCooldown(2, 5f), Is.True);
    }

    [Test]
    public void ConcurrentCallers_OnlyOneSucceeds()
    {
        const ulong userId = 42;
        var successCount = 0;

        Parallel.For(0, 100, _ =>
        {
            if (_map.TryAddCooldown(userId, 5f))
                Interlocked.Increment(ref successCount);
        });

        Assert.That(successCount, Is.EqualTo(1));
    }
}
