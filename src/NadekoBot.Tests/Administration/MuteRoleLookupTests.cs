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
    public void FindsRoleIgnoringCase()
    {
        var guild = GuildWith(Role(1, "member", 5), Role(2, "Nadeko-Mute", 3));

        Assert.That(MuteService.FindRoleByName(guild, "nadeko-mute")?.Id, Is.EqualTo(2));
        Assert.That(MuteService.FindRoleByName(guild, "missing"), Is.Null);
    }

    [Test]
    public void PrefersLowestPosition_WhenNamesCollide()
    {
        var guild = GuildWith(
            Role(1, "nadeko-mute", 20),
            Role(2, "nadeko-mute", 2),
            Role(3, "nadeko-mute", 11));

        Assert.That(MuteService.FindRoleByName(guild, "nadeko-mute")?.Id, Is.EqualTo(2));
    }
}
