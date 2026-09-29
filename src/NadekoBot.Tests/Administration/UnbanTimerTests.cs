using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common;
using NadekoBot.Db.Models;
using NadekoBot.Extensions;
using NadekoBot.Modules.Administration.Services;
using NadekoBot.Services;
using NadekoBot.Tests.Waifu;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Administration;

public class UnbanTimerTests
{
    private const ulong GUILD_ID = 10;
    private const ulong OTHER_GUILD_ID = 20;

    private TestDbService _db = null!;
    private MuteService _svc = null!;

    [SetUp]
    public void Setup()
    {
        _db = new TestDbService();
        var creds = Substitute.For<IBotCreds>();
        creds.TotalShards.Returns(1);
        var client = Substitute.For<DiscordSocketClient>();
        _svc = new MuteService(client, _db, Substitute.For<IMessageSenderService>(), new ShardData(client, creds));
    }

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    private static IGuild Guild(ulong id)
    {
        var guild = Substitute.For<IGuild>();
        guild.Id.Returns(id);
        return guild;
    }

    private async Task<ulong[]> TimerUsersAsync(ulong guildId)
    {
        await using var ctx = _db.GetDbContext();
        return await ctx.GetTable<UnbanTimer>()
                        .Where(x => x.GuildId == guildId)
                        .OrderBy(x => x.UserId)
                        .Select(x => x.UserId)
                        .ToArrayAsyncLinqToDB();
    }

    [Test]
    public async Task PermanentBanAndUnban_RemoveOnlyThatUsersTimer()
    {
        var guild = Guild(GUILD_ID);
        await _svc.TimedBan(guild, 1, TimeSpan.FromDays(1), "r", 0);
        await _svc.TimedBan(guild, 2, TimeSpan.FromDays(1), "r", 0);
        await _svc.TimedBan(guild, 3, TimeSpan.FromDays(1), "r", 0);
        await _svc.TimedBan(Guild(OTHER_GUILD_ID), 1, TimeSpan.FromDays(1), "r", 0);

        await _svc.BanAsync(guild, 1, 0, "permanent");
        await guild.Received(1).AddBanAsync(1UL, 0, "permanent");
        Assert.That(await TimerUsersAsync(GUILD_ID), Is.EqualTo(new ulong[] { 2, 3 }));

        await _svc.ClearUnbanTimersAsync(GUILD_ID, [3]);
        Assert.That(await TimerUsersAsync(GUILD_ID), Is.EqualTo(new ulong[] { 2 }));
        Assert.That(await TimerUsersAsync(OTHER_GUILD_ID), Is.EqualTo(new ulong[] { 1 }));
    }
}
