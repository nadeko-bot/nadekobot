using System.Collections.Generic;
using Discord;
using NadekoBot.Modules.Administration.Services;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Administration;

public class MuteRoleLookupTests
{
    private static IRole Role(ulong id, string name, int position)
    {
        var role = Substitute.For<IRole>();
        role.Id.Returns(id);
        role.Name.Returns(name);
        role.Position.Returns(position);
        return role;
    }

    private static IGuild GuildWith(params IRole[] roles)
    {
        var guild = Substitute.For<IGuild>();
        guild.Roles.Returns(new List<IRole>(roles));
        return guild;
    }

    [Test]
    public void FindRoleByName_ReturnsMatchingRole()
    {
        var guild = GuildWith(Role(1, "member", 5), Role(2, "nadeko-mute", 3));

        var found = MuteService.FindRoleByName(guild, "nadeko-mute");

        Assert.That(found?.Id, Is.EqualTo(2));
    }

    [Test]
    public void FindRoleByName_ReturnsNull_WhenNoRoleMatches()
    {
        var guild = GuildWith(Role(1, "member", 5));

        Assert.That(MuteService.FindRoleByName(guild, "nadeko-mute"), Is.Null);
    }

    [Test]
    public void FindRoleByName_PrefersLowestPosition_WhenNamesCollide()
    {
        var guild = GuildWith(
            Role(1, "nadeko-mute", 20),
            Role(2, "nadeko-mute", 2),
            Role(3, "nadeko-mute", 11));

        var found = MuteService.FindRoleByName(guild, "nadeko-mute");

        Assert.That(found?.Id, Is.EqualTo(2));
    }

    [Test]
    public void FindRoleByName_IsCaseInsensitive()
    {
        var guild = GuildWith(Role(1, "Nadeko-Mute", 3));

        Assert.That(MuteService.FindRoleByName(guild, "nadeko-mute")?.Id, Is.EqualTo(1));
    }
}
