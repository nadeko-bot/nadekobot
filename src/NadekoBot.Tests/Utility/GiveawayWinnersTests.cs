using System;
using System.Linq;
using System.Threading.Tasks;
using Discord.WebSocket;
using Microsoft.Extensions.Caching.Memory;
using Nadeko.Common;
using NadekoBot.Common;
using NadekoBot.Extensions;
using NadekoBot.Modules.Utility;
using NadekoBot.Services;
using NadekoBot.Tests.Waifu;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Utility;

public class GiveawayWinnersTests
{
    private const ulong GUILD_ID = 111;
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
    public async Task WinnerCountAndPicks_StayInRangeAndRerollPicksSomeoneElse()
    {
        Assert.That(GiveawayService.TryParseWinnerCount("3w", out var three) && three == 3, Is.True);
        Assert.That(GiveawayService.TryParseWinnerCount($"{GiveawayService.MAX_WINNERS}W", out _), Is.True);

        foreach (var bad in new[] { "0w", $"{GiveawayService.MAX_WINNERS + 1}w", "w", "3", "-3w", "3x", "Nitro" })
            Assert.That(GiveawayService.TryParseWinnerCount(bad, out _), Is.False, bad);

        var id = await _svc.StartGiveawayAsync(GUILD_ID, 1, MESSAGE_ID, TimeSpan.FromHours(1), "prize", 3);
        Assert.That(id, Is.Not.Null);

        for (ulong userId = 1; userId <= 4; userId++)
            await _svc.JoinGivawayAsync(MESSAGE_ID, userId, $"user{userId}");

        var participants = Enumerable.Range(1, 5)
                                     .Select(x => new NadekoBot.Db.Models.GiveawayUser { UserId = (ulong)x })
                                     .ToList();
        var winners = GiveawayService.PickWinners(participants, 3, new NadekoRandom());
        Assert.That(winners.Select(x => x.UserId).Distinct().Count(), Is.EqualTo(3));
        Assert.That(participants.Select(x => x.UserId).Intersect(winners.Select(x => x.UserId)), Is.Empty);
        Assert.That(participants, Has.Count.EqualTo(2));

        // more winners than entries picks everyone once
        Assert.That(GiveawayService.PickWinners(participants, 3, new NadekoRandom()), Has.Length.EqualTo(2));

        Assert.That(await _svc.EndGiveawayAsync(GUILD_ID, id!.Value), Is.True);
        // 4 entries, 3 winners: one entry is left for a reroll
        Assert.That(await _svc.RerollGiveawayAsync(GUILD_ID, id.Value), Is.True);
        Assert.That(await _svc.RerollGiveawayAsync(GUILD_ID, id.Value), Is.False);
    }
}
