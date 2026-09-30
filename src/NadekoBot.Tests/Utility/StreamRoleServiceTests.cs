using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using NadekoBot.Common;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Utility.Common;
using NadekoBot.Modules.Utility.Services;
using NadekoBot.Tests.Waifu;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Utility;

public class StreamRoleServiceTests
{
    private const ulong GUILD = 1;
    private const ulong USER = 5;

    private TestDbService _db = null!;
    private StreamRoleService _svc = null!;
    private IGuild _guild = null!;

    [SetUp]
    public void Setup()
    {
        _db = new TestDbService();
        _svc = new StreamRoleService(Substitute.For<DiscordSocketClient>(), _db);
        _guild = Substitute.For<IGuild>();
        _guild.Id.Returns(GUILD);
    }

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    [Test]
    public async Task ApplyListAction_AddRemoveWork_AndRemoveClearsDuplicates()
    {
        await using (var ctx = _db.GetDbContext())
        {
            var settings = new StreamRoleSettings { GuildId = GUILD };
            ctx.Add(settings);
            await ctx.SaveChangesAsync();

            for (var i = 0; i < 2; i++)
            {
                ctx.Add(new StreamRoleWhitelistedUser { StreamRoleSettingsId = settings.Id, UserId = USER, Username = "u" });
                ctx.Add(new StreamRoleBlacklistedUser { StreamRoleSettingsId = settings.Id, UserId = USER, Username = "u" });
            }

            await ctx.SaveChangesAsync();
        }

        foreach (var list in new[] { StreamRoleListType.Whitelist, StreamRoleListType.Blacklist })
        {
            Assert.That(await _svc.ApplyListAction(list, _guild, AddRemove.Add, USER, "u"), Is.False);
            Assert.That(await _svc.ApplyListAction(list, _guild, AddRemove.Rem, USER, "u"), Is.True);
            Assert.That(await _svc.ApplyListAction(list, _guild, AddRemove.Rem, USER, "u"), Is.False);
            Assert.That(await _svc.ApplyListAction(list, _guild, AddRemove.Add, USER, "u"), Is.True);
        }

        await using var check = _db.GetDbContext();
        Assert.That(check.Set<StreamRoleWhitelistedUser>().Count(x => x.UserId == USER), Is.EqualTo(1));
        Assert.That(check.Set<StreamRoleBlacklistedUser>().Count(x => x.UserId == USER), Is.EqualTo(1));
    }

    [Test]
    public void StreamChanged_DetectsStreamStartStopAndChange()
    {
        IActivity[] game = [new Game("Some Game")];
        IActivity[] stream = [new StreamingGame("My stream", "https://twitch.tv/me")];
        IActivity[] otherTitle = [new StreamingGame("Another title", "https://twitch.tv/me")];

        Assert.That(StreamRoleService.StreamChanged(game, stream), Is.True);
        Assert.That(StreamRoleService.StreamChanged(stream, game), Is.True);
        Assert.That(StreamRoleService.StreamChanged(null, stream), Is.True);
        Assert.That(StreamRoleService.StreamChanged(stream, otherTitle), Is.True);

        Assert.That(StreamRoleService.StreamChanged(stream, [new StreamingGame("My stream", "https://twitch.tv/me")]),
            Is.False);
        Assert.That(StreamRoleService.StreamChanged(game, [new Game("Other Game"), new Game("Third")]), Is.False);
    }
}
