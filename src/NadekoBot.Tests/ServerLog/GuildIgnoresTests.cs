using System.Collections.Generic;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Administration;
using NUnit.Framework;

namespace NadekoBot.Tests.ServerLog;

public class GuildIgnoresTests
{
    [Test]
    public void Users_ContainsOnlyUserIds()
    {
        var ignores = new List<LogIgnore>
        {
            new() { GuildId = 1, LogItemId = 100, ItemType = IgnoredItemType.User },
            new() { GuildId = 1, LogItemId = 200, ItemType = IgnoredItemType.Channel },
            new() { GuildId = 1, LogItemId = 300, ItemType = IgnoredItemType.Category },
        };

        var gi = new GuildIgnores(ignores);

        Assert.That(gi.Users.Contains(100), Is.True);
        Assert.That(gi.Users.Contains(200), Is.False);
        Assert.That(gi.Users.Contains(300), Is.False);
        Assert.That(gi.Users, Has.Count.EqualTo(1));
    }

    [Test]
    public void Channels_ContainsOnlyChannelIds()
    {
        var ignores = new List<LogIgnore>
        {
            new() { GuildId = 1, LogItemId = 10, ItemType = IgnoredItemType.User },
            new() { GuildId = 1, LogItemId = 20, ItemType = IgnoredItemType.Channel },
            new() { GuildId = 1, LogItemId = 30, ItemType = IgnoredItemType.Channel },
        };

        var gi = new GuildIgnores(ignores);

        Assert.That(gi.Channels.Contains(20), Is.True);
        Assert.That(gi.Channels.Contains(30), Is.True);
        Assert.That(gi.Channels.Contains(10), Is.False);
        Assert.That(gi.Channels, Has.Count.EqualTo(2));
    }

    [Test]
    public void Categories_ContainsOnlyCategoryIds()
    {
        var ignores = new List<LogIgnore>
        {
            new() { GuildId = 1, LogItemId = 50, ItemType = IgnoredItemType.Category },
            new() { GuildId = 1, LogItemId = 60, ItemType = IgnoredItemType.User },
        };

        var gi = new GuildIgnores(ignores);

        Assert.That(gi.Categories.Contains(50), Is.True);
        Assert.That(gi.Categories.Contains(60), Is.False);
        Assert.That(gi.Categories, Has.Count.EqualTo(1));
    }

    [Test]
    public void All_PreservesInput()
    {
        var ignores = new List<LogIgnore>
        {
            new() { GuildId = 1, LogItemId = 1, ItemType = IgnoredItemType.User },
            new() { GuildId = 1, LogItemId = 2, ItemType = IgnoredItemType.Channel },
            new() { GuildId = 1, LogItemId = 3, ItemType = IgnoredItemType.Category },
        };

        var gi = new GuildIgnores(ignores);

        Assert.That(gi.All, Has.Count.EqualTo(3));
        Assert.That(gi.All[0].LogItemId, Is.EqualTo(1));
        Assert.That(gi.All[1].LogItemId, Is.EqualTo(2));
        Assert.That(gi.All[2].LogItemId, Is.EqualTo(3));
    }

    [Test]
    public void Empty_Input_YieldsEmptySets()
    {
        var gi = new GuildIgnores([]);

        Assert.That(gi.Users, Is.Empty);
        Assert.That(gi.Channels, Is.Empty);
        Assert.That(gi.Categories, Is.Empty);
        Assert.That(gi.All, Is.Empty);
    }
}
