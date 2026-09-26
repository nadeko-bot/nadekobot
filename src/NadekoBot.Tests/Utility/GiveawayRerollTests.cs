using System;
using System.Threading.Tasks;
using Discord.WebSocket;
using Microsoft.Extensions.Caching.Memory;
using NadekoBot.Common;
using NadekoBot.Extensions;
using NadekoBot.Modules.Utility;
using NadekoBot.Services;
using NadekoBot.Tests.Waifu;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Utility;

public class GiveawayRerollTests
{
    private const ulong GUILD_ID = 111;
    private const ulong OTHER_GUILD_ID = 222;
    private const ulong MESSAGE_ID = 333;

    private TestDbService _db = null!;
    private MemoryCache _cache = null!;
    private GiveawayService _svc = null!;

    [SetUp]
    public void Setup()
    {
        _db = new TestDbService();
        _cache = new MemoryCache(new MemoryCacheOptions());

        _svc = new GiveawayService(_db,
            Substitute.For<IBotCreds>(),
            Substitute.For<DiscordSocketClient>(),
            Substitute.For<IMessageSenderService>(),
            Substitute.For<IBotStrings>(),
            Substitute.For<ILocalization>(),
            _cache);
    }

    [TearDown]
    public void TearDown()
    {
        _cache.Dispose();
        _db.Dispose();
    }

    [Test]
    public async Task Reroll_OnlyWorksInTheGiveawayServer()
    {
        var id = await _svc.StartGiveawayAsync(GUILD_ID, 1, MESSAGE_ID, TimeSpan.FromHours(1), "prize");
        Assert.That(id, Is.Not.Null);

        for (ulong userId = 1; userId <= 3; userId++)
            await _svc.JoinGivawayAsync(MESSAGE_ID, userId, $"user{userId}");

        Assert.That(await _svc.EndGiveawayAsync(GUILD_ID, id!.Value), Is.True);

        Assert.That(await _svc.RerollGiveawayAsync(OTHER_GUILD_ID, id.Value), Is.False);
        Assert.That(await _svc.RerollGiveawayAsync(GUILD_ID, id.Value), Is.True);
    }
}
