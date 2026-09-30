using System.Threading.Tasks;
using Discord.WebSocket;
using NadekoBot.Common;
using NadekoBot.Modules.Administration.Services;
using NadekoBot.Tests.Waifu;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Administration;

public class AutoAssignRoleServiceTests
{
    private const ulong GUILD = 1;

    private TestDbService _db = null!;
    private AutoAssignRoleService _svc = null!;

    [SetUp]
    public void Setup()
    {
        _db = new TestDbService();
        var client = Substitute.For<DiscordSocketClient>();
        var creds = Substitute.For<IBotCreds>();
        creds.TotalShards.Returns(1);
        _svc = new AutoAssignRoleService(client, _db, new ShardData(client, creds));
    }

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    [Test]
    public async Task Toggle_RespectsLimit_AndDisablesWhenEmpty()
    {
        for (ulong role = 10; role < 10 + AutoAssignRoleService.MAX_ROLES; role++)
            Assert.That((await _svc.ToggleAarAsync(GUILD, role)).Result, Is.EqualTo(AarToggleResult.Added));

        var (result, roles) = await _svc.ToggleAarAsync(GUILD, 99);
        Assert.That(result, Is.EqualTo(AarToggleResult.LimitReached));
        Assert.That(roles, Is.EqualTo(new ulong[] { 10, 11, 12 }));

        Assert.That((await _svc.ToggleAarAsync(GUILD, 11)).Result, Is.EqualTo(AarToggleResult.Removed));
        Assert.That((await _svc.ToggleAarAsync(GUILD, 10)).Result, Is.EqualTo(AarToggleResult.Removed));
        Assert.That((await _svc.ToggleAarAsync(GUILD, 12)).Result, Is.EqualTo(AarToggleResult.Disabled));
        Assert.That(_svc.TryGetRoles(GUILD, out _), Is.False);
    }
}
