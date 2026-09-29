using System;
using System.Linq;
using System.Threading.Tasks;
using Discord.WebSocket;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Nadeko.Common;
using NadekoBot.Common;
using NadekoBot.Modules.Utility;
using NadekoBot.Modules.Utility.Scheduled;
using NadekoBot.Services;
using NadekoBot.Tests.Waifu;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Utility;

public class ScheduleCommandServiceTests
{
    private const ulong GUILD = 1;
    private const ulong USER = 2;

    private TestDbService _db = null!;
    private ScheduleCommandService _svc = null!;

    [SetUp]
    public void Setup()
    {
        _db = new TestDbService();
        var client = Substitute.For<DiscordSocketClient>();
        var creds = Substitute.For<IBotCreds>();
        creds.TotalShards.Returns(1);

        _svc = new ScheduleCommandService(_db,
            Substitute.For<ICommandHandler>(),
            client,
            new ShardData(client, creds),
            new UtilityConfigService(new YamlSeria(), Substitute.For<IPubSub>()));
    }

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    [Test]
    public async Task RemovesDueCommands_AndKeepsLongSchedules()
    {
        Assert.That(await _svc.AddScheduledCommandAsync(GUILD, 3, 4, USER, ".say later", TimeSpan.FromDays(60)), Is.True);

        await using (var ctx = _db.GetDbContext())
        {
            await ctx.GetTable<ScheduledCommand>()
                .InsertAsync(() => new()
                {
                    GuildId = GUILD,
                    UserId = USER,
                    ChannelId = 3,
                    MessageId = 5,
                    Text = ".say now",
                    When = DateTime.UtcNow.AddMinutes(-1)
                });
        }

        await _svc.ProcessNextAsync();

        var remaining = (await _svc.GetUserScheduledCommandsAsync(GUILD, USER)).ToArray();
        Assert.That(remaining.Select(x => x.Text), Is.EqualTo(new[] { ".say later" }));
        Assert.That(remaining[0].When.Kind, Is.EqualTo(DateTimeKind.Utc));

        // the next step only waits for the 60-day schedule; it must not throw or delete it
        var waiting = _svc.ProcessNextAsync();
        await Task.WhenAny(waiting, Task.Delay(300));
        Assert.That(waiting.IsFaulted, Is.False);
        Assert.That(await _svc.GetUserScheduledCommandsAsync(GUILD, USER), Has.Count.EqualTo(1));
    }
}
