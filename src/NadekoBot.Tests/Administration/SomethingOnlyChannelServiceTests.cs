using System.Linq;
using System.Threading.Tasks;
using Discord.WebSocket;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Administration.Services;
using NadekoBot.Tests.Waifu;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Administration;

public class SomethingOnlyChannelServiceTests
{
    private const ulong GUILD = 1;
    private const ulong CHANNEL = 2;

    private TestDbService _db = null!;
    private SomethingOnlyChannelService _svc = null!;

    [SetUp]
    public void Setup()
    {
        _db = new TestDbService();
        _svc = new SomethingOnlyChannelService(new MemoryCache(new MemoryCacheOptions()),
            Substitute.For<DiscordSocketClient>(),
            _db);
    }

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    private async Task<OnlyChannelType[]> GetRowsAsync()
    {
        await using var ctx = _db.GetDbContext();
        return await ctx.GetTable<ImageOnlyChannel>()
            .Where(x => x.ChannelId == CHANNEL)
            .Select(x => x.Type)
            .ToArrayAsyncLinqToDB();
    }

    [Test]
    public async Task Toggle_SwitchesTypeAndTurnsOff()
    {
        Assert.That(await _svc.ToggleAsync(GUILD, CHANNEL, OnlyChannelType.Image), Is.True);
        Assert.That(await _svc.ToggleAsync(GUILD, CHANNEL, OnlyChannelType.Link), Is.True);
        Assert.That(_svc.GetMode(GUILD, CHANNEL), Is.EqualTo(OnlyChannelType.Link));
        Assert.That(await GetRowsAsync(), Is.EqualTo(new[] { OnlyChannelType.Link }));

        Assert.That(await _svc.ToggleAsync(GUILD, CHANNEL, OnlyChannelType.Link), Is.False);
        Assert.That(_svc.GetMode(GUILD, CHANNEL), Is.Null);
        Assert.That(await GetRowsAsync(), Is.Empty);
    }

    [Test]
    public async Task ForcedDisable_StopsLinkOnlyChannelInMemoryAndDb()
    {
        await _svc.ToggleAsync(GUILD, CHANNEL, OnlyChannelType.Link);

        await _svc.DisableAsync(GUILD, CHANNEL);

        Assert.That(_svc.GetMode(GUILD, CHANNEL), Is.Null);
        Assert.That(await GetRowsAsync(), Is.Empty);
    }
}
