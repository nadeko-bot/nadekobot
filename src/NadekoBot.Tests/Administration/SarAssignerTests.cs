using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using NadekoBot.Common;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Administration.Services;
using NadekoBot.Modules.Xp;
using NadekoBot.Tests.Waifu;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Administration;

public class SarAssignerTests
{
    private const ulong GUILD = 1;
    private const ulong BOT_ROLE = 100;
    private const ulong LOW = 10;
    private const ulong LOW2 = 11;
    private const ulong HIGH = 20;
    private const ulong DELETED_REQ = 99;

    private TestDbService _db = null!;
    private SarAssignerService _svc = null!;
    private IGuild _guild = null!;

    [SetUp]
    public void Setup()
    {
        _db = new TestDbService();
        var client = Substitute.For<DiscordSocketClient>();
        _svc = new SarAssignerService(_db,
            new XpFormulaService(_db, new ShardData(client, Substitute.For<IBotCreds>())));

        var roles = new Dictionary<ulong, IRole>
        {
            [BOT_ROLE] = Role(BOT_ROLE, 5),
            [LOW] = Role(LOW, 1),
            [LOW2] = Role(LOW2, 2),
            [HIGH] = Role(HIGH, 9),
        };

        _guild = Substitute.For<IGuild>();
        _guild.Id.Returns(GUILD);
        _guild.OwnerId.Returns(12345UL);
        _guild.GetRole(Arg.Any<ulong>()).Returns(ci => roles.GetValueOrDefault(ci.Arg<ulong>()));

        var bot = Substitute.For<IGuildUser>();
        bot.Id.Returns(2UL);
        bot.RoleIds.Returns(new[] { BOT_ROLE });
        _guild.GetCurrentUserAsync().Returns(bot);
    }

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    [Test]
    public async Task Assign_IgnoresDeletedRequirementAndChecksHierarchy()
    {
        var group = new SarGroup
        {
            GuildId = GUILD,
            IsExclusive = true,
            RoleReq = DELETED_REQ,
            Roles = [Sar(LOW), Sar(LOW2), Sar(HIGH)]
        };

        var user = Substitute.For<IGuildUser>();
        user.Guild.Returns(_guild);
        user.RoleIds.Returns(new[] { LOW2, HIGH });

        Assert.That((await _svc.AssignAsync(group, user, HIGH)).IsT4, Is.True, "already has the role");

        var result = await _svc.AssignAsync(group, user, LOW);
        Assert.That(result.IsT0, Is.True);
        // HIGH is above the bot, so only LOW2 can be taken away
        await user.Received(1).RemoveRolesAsync(Arg.Is<IEnumerable<ulong>>(x => Equal(x, LOW2)));
        await user.Received(1).AddRoleAsync(LOW);

        var other = Substitute.For<IGuildUser>();
        other.Guild.Returns(_guild);
        other.RoleIds.Returns(new ulong[0]);
        Assert.That((await _svc.AssignAsync(group, other, HIGH)).IsT5, Is.True, "role above the bot");
        await other.DidNotReceive().AddRoleAsync(Arg.Any<ulong>());
    }

    private static bool Equal(IEnumerable<ulong> ids, ulong only)
    {
        var list = new List<ulong>(ids);
        return list.Count == 1 && list[0] == only;
    }

    private static Sar Sar(ulong roleId)
        => new() { GuildId = GUILD, RoleId = roleId };

    private static IRole Role(ulong id, int position)
    {
        var role = Substitute.For<IRole>();
        role.Id.Returns(id);
        role.Position.Returns(position);
        return role;
    }
}
