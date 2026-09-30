using System.Linq;
using System.Threading.Tasks;
using Discord.WebSocket;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common;
using NadekoBot.Db;
using NadekoBot.Db.Models;
using NadekoBot.Extensions;
using NadekoBot.Modules.Administration.Services;
using NadekoBot.Modules.Utility.Services;
using NadekoBot.Services;
using NadekoBot.Tests.Waifu;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Administration;

public class GuildConfigRowTests
{
    private const ulong GUILD_ID = 111;

    private TestDbService _db = null!;
    private ShardData _shardData = null!;
    private DiscordSocketClient _client = null!;

    [SetUp]
    public void Setup()
    {
        _db = new TestDbService();
        _client = Substitute.For<DiscordSocketClient>();
        var creds = Substitute.For<IBotCreds>();
        creds.TotalShards.Returns(1);
        _shardData = new ShardData(_client, creds);
    }

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    [Test]
    public async Task SettingsAreSaved_OnServerWithoutConfigRow()
    {
        var mute = new MuteService(_client, _db, Substitute.For<IMessageSenderService>(), _shardData);
        await mute.SetMuteRoleAsync(GUILD_ID, 42);

        var verbose = new VerboseErrorsService(_db,
            null!,
            Substitute.For<IMessageSenderService>(),
            Substitute.For<ICommandsUtilityService>(),
            _shardData);
        Assert.That(await verbose.ToggleVerboseErrors(GUILD_ID), Is.False);
        Assert.That(await verbose.ToggleVerboseErrors(GUILD_ID), Is.True);
        Assert.That(await verbose.ToggleVerboseErrors(GUILD_ID, false), Is.False);

        await using var ctx = _db.GetDbContext();
        await ctx.EnsureGuildConfigAsync(GUILD_ID);

        var rows = await ctx.GetTable<GuildConfig>()
            .Where(x => x.GuildId == GUILD_ID)
            .Select(x => new { x.MuteRoleId, x.VerboseErrors })
            .ToListAsyncLinqToDB();

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].MuteRoleId, Is.EqualTo(42));
        Assert.That(rows[0].VerboseErrors, Is.False);
    }
}
