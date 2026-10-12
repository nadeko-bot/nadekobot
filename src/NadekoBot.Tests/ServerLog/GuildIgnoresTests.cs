using System.Collections.Generic;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Administration;
using NUnit.Framework;

namespace NadekoBot.Tests.ServerLog;

public class GuildIgnoresTests
{
    [Test]
    public void GuildIgnores_SplitsIdsByType_AndKeepsInputOrder()
    {
        var ignores = new List<LogIgnore>
        {
            new() { GuildId = 1, LogItemId = 100, ItemType = IgnoredItemType.User },
            new() { GuildId = 1, LogItemId = 20, ItemType = IgnoredItemType.Channel },
            new() { GuildId = 1, LogItemId = 30, ItemType = IgnoredItemType.Channel },
            new() { GuildId = 1, LogItemId = 50, ItemType = IgnoredItemType.Category },
        };

        var gi = new GuildIgnores(ignores);

        Assert.That(gi.Users, Is.EquivalentTo(new ulong[] { 100 }));
        Assert.That(gi.Channels, Is.EquivalentTo(new ulong[] { 20, 30 }));
        Assert.That(gi.Categories, Is.EquivalentTo(new ulong[] { 50 }));

        Assert.That(gi.All, Has.Count.EqualTo(4));
        Assert.That(gi.All[0].LogItemId, Is.EqualTo(100));
        Assert.That(gi.All[1].LogItemId, Is.EqualTo(20));
        Assert.That(gi.All[2].LogItemId, Is.EqualTo(30));
        Assert.That(gi.All[3].LogItemId, Is.EqualTo(50));

        var empty = new GuildIgnores([]);

        Assert.That(empty.Users, Is.Empty);
        Assert.That(empty.Channels, Is.Empty);
        Assert.That(empty.Categories, Is.Empty);
        Assert.That(empty.All, Is.Empty);
    }
}
