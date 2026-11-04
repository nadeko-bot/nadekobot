#nullable enable
using System.Threading.Tasks;
using Discord.WebSocket;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Xp;
using NadekoBot.Tests.Waifu;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Xp;

public sealed class XpExclusionTests
{
    private const ulong GUILD_ID = 111;
    private const ulong OTHER_GUILD_ID = 222;

    private TestDbService _db = null!;
    private XpExclusionService _svc = null!;

    [SetUp]
    public void SetUp()
    {
        _db = new TestDbService();
        var creds = Substitute.For<IBotCreds>();
        creds.TotalShards.Returns(1);
        _svc = new XpExclusionService(_db, new ShardData(Substitute.For<DiscordSocketClient>(), creds));
    }

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    [Test]
    public async Task HasRoleExclusions_FollowsTheRoleToggles()
    {
        await _svc.ToggleExclusionAsync(GUILD_ID, XpExcludedItemType.User, 5);
        Assert.That(_svc.HasRoleExclusions(GUILD_ID), Is.False);

        await _svc.ToggleExclusionAsync(GUILD_ID, XpExcludedItemType.Role, 1);
        await _svc.ToggleExclusionAsync(GUILD_ID, XpExcludedItemType.Role, 2);
        await _svc.ToggleExclusionAsync(GUILD_ID, XpExcludedItemType.Role, 1);

        Assert.That(_svc.HasRoleExclusions(GUILD_ID), Is.True);
        Assert.That(_svc.IsExcluded(GUILD_ID, XpExcludedItemType.Role, 2), Is.True);
        Assert.That(_svc.HasRoleExclusions(OTHER_GUILD_ID), Is.False);

        await _svc.ToggleExclusionAsync(GUILD_ID, XpExcludedItemType.Role, 2);
        Assert.That(_svc.HasRoleExclusions(GUILD_ID), Is.False);
    }

    [Test]
    public async Task OnReady_LoadsRoleExclusionsOnlyForServersWhichHaveThem()
    {
        await using (var ctx = _db.GetDbContext())
        {
            await ctx.GetTable<XpExcludedItem>().InsertAsync(() => new() { GuildId = GUILD_ID, ItemType = XpExcludedItemType.Role, ItemId = 1 });
            await ctx.GetTable<XpExcludedItem>().InsertAsync(() => new() { GuildId = OTHER_GUILD_ID, ItemType = XpExcludedItemType.User, ItemId = 5 });
        }

        await _svc.OnReadyAsync();

        Assert.That(_svc.HasRoleExclusions(GUILD_ID), Is.True);
        Assert.That(_svc.IsExcluded(GUILD_ID, XpExcludedItemType.Role, 1), Is.True);
        Assert.That(_svc.HasRoleExclusions(OTHER_GUILD_ID), Is.False);
        Assert.That(_svc.IsExcluded(OTHER_GUILD_ID, XpExcludedItemType.User, 5), Is.True);
    }
}
