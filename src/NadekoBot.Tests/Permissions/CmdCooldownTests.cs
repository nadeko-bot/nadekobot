using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Permissions.Services;
using NadekoBot.Services;
using NadekoBot.Tests.Waifu;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Permissions;

public class CmdCooldownTests
{
    private const ulong GUILD_ID = 10;

    private TestDbService _db = null!;
    private CmdCdService _svc = null!;
    private IGuild _guild = null!;
    private IUser _user = null!;

    [SetUp]
    public void Setup()
    {
        _db = new TestDbService();
        var creds = Substitute.For<IBotCreds>();
        creds.TotalShards.Returns(1);
        _svc = new CmdCdService(_db, new ShardData(Substitute.For<DiscordSocketClient>(), creds));

        _guild = Substitute.For<IGuild>();
        _guild.Id.Returns(GUILD_ID);
        _user = Substitute.For<IUser>();
        _user.Id.Returns(100UL);
    }

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    private async Task<int> RowCountAsync()
    {
        await using var ctx = _db.GetDbContext();
        return await ctx.GetTable<CommandCooldown>().CountAsyncLinqToDB(x => x.GuildId == GUILD_ID);
    }

    [Test]
    public async Task SetCooldown_ZeroRemovesTheCooldown()
    {
        await _svc.SetCooldownAsync(GUILD_ID, "Hug", 60);

        Assert.That(await _svc.TryBlock(_guild, _user, "hug"), Is.False);
        Assert.That(await _svc.TryBlock(_guild, _user, "hug"), Is.True);
        Assert.That(await RowCountAsync(), Is.EqualTo(1));

        await _svc.SetCooldownAsync(GUILD_ID, "HUG", 0);

        Assert.That(await _svc.TryBlock(_guild, _user, "hug"), Is.False);
        Assert.That(await _svc.TryBlock(_guild, _user, "hug"), Is.False);
        Assert.That(_svc.GetCommandCooldowns(GUILD_ID), Is.Empty);
        Assert.That(await RowCountAsync(), Is.EqualTo(0));
    }
}
