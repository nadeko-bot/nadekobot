using System.Collections.Generic;
using System.Linq;
using Discord;
using NadekoBot.Modules.Administration;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Administration;

public class StickyRolesFilterTests
{
    private const ulong EVERYONE_ID = 1;
    private const int BOT_HIERARCHY = 10;

    private static IRole Role(ulong id, int position, bool managed = false)
    {
        var role = Substitute.For<IRole>();
        role.Id.Returns(id);
        role.Position.Returns(position);
        role.IsManaged.Returns(managed);
        return role;
    }

    private static IGuild GuildWith(params IRole[] roles)
    {
        var everyone = Role(EVERYONE_ID, 0);
        var known = roles.ToDictionary(x => x.Id);
        known[EVERYONE_ID] = everyone;

        var guild = Substitute.For<IGuild>();
        guild.EveryoneRole.Returns(everyone);
        guild.GetRole(Arg.Any<ulong>())
            .Returns(call => known.GetValueOrDefault(call.Arg<ulong>()));

        return guild;
    }

    [Test]
    public void KeepsAssignableRoles()
    {
        var guild = GuildWith(Role(100, 3), Role(200, 5));

        var result = StickyRolesService.GetAssignableRoles([100, 200], guild, BOT_HIERARCHY);

        Assert.That(result.Select(x => x.Id), Is.EquivalentTo(new ulong[] { 100, 200 }));
    }

    [Test]
    public void SkipsDeletedRole_ButKeepsTheRest()
    {
        var guild = GuildWith(Role(100, 3));

        var result = StickyRolesService.GetAssignableRoles([100, 999], guild, BOT_HIERARCHY);

        Assert.That(result.Select(x => x.Id), Is.EquivalentTo(new ulong[] { 100 }));
    }

    [Test]
    public void SkipsRoleAtOrAboveBotHierarchy()
    {
        var guild = GuildWith(Role(100, 3), Role(200, BOT_HIERARCHY), Role(300, BOT_HIERARCHY + 1));

        var result = StickyRolesService.GetAssignableRoles([100, 200, 300], guild, BOT_HIERARCHY);

        Assert.That(result.Select(x => x.Id), Is.EquivalentTo(new ulong[] { 100 }));
    }

    [Test]
    public void SkipsManagedAndEveryoneRoles()
    {
        var guild = GuildWith(Role(100, 3), Role(200, 4, managed: true));

        var result = StickyRolesService.GetAssignableRoles([100, 200, EVERYONE_ID], guild, BOT_HIERARCHY);

        Assert.That(result.Select(x => x.Id), Is.EquivalentTo(new ulong[] { 100 }));
    }

    [Test]
    public void ReturnsEmpty_WhenNothingIsAssignable()
    {
        var guild = GuildWith(Role(100, BOT_HIERARCHY + 2));

        var result = StickyRolesService.GetAssignableRoles([100, 999], guild, BOT_HIERARCHY);

        Assert.That(result, Is.Empty);
    }
}
