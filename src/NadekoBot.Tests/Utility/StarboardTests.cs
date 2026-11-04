using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using System.Linq;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common;
using NadekoBot.Extensions;
using NadekoBot.Modules.Utility.Starboard;
using NadekoBot.Modules.Utility.Starboard.Db;
using NadekoBot.Services;
using NadekoBot.Tests.Waifu;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Utility;

public class StarboardTests
{
    private const ulong GUILD_ID = 111;
    private const ulong CHANNEL_ID = 222;

    private TestDbService _db = null!;
    private StarboardService _svc = null!;

    [SetUp]
    public void Setup()
    {
        _db = new TestDbService();

        var client = Substitute.For<DiscordSocketClient>();
        var creds = Substitute.For<IBotCreds>();
        creds.TotalShards.Returns(1);

        _svc = new StarboardService(_db,
            client,
            new ShardData(client, creds),
            Substitute.For<IMessageSenderService>(),
            Substitute.For<IBotStrings>(),
            IntentState.All);
    }

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    [Test]
    public async Task SetChannel_CreatesDefaults_KeepsSettingsAndForgetsOldBoard()
    {
        await _svc.SetChannelAsync(GUILD_ID, CHANNEL_ID);
        await _svc.SetThresholdAsync(GUILD_ID, 7);
        await SeedEntriesAsync(12);

        await using (var uow = _db.GetDbContext())
        {
            await uow.GetTable<StarboardMessage>()
                     .InsertAsync(() => new() { GuildId = GUILD_ID, Index = 0, MessageId = 5001 });
            await uow.GetTable<StarboardMessage>()
                     .InsertAsync(() => new() { GuildId = GUILD_ID, Index = 1, MessageId = 5002 });
        }

        await _svc.SetChannelAsync(GUILD_ID, 999);

        await using var uow2 = _db.GetDbContext();
        var rows = await uow2.GetTable<StarboardConfig>().ToListAsyncLinqToDB();

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].ChannelId, Is.EqualTo(999));
        Assert.That(rows[0].Threshold, Is.EqualTo(7));
        Assert.That(rows[0].Emote, Is.EqualTo(StarboardConsts.DEFAULT_EMOTE));
        Assert.That(rows[0].IsEnabled, Is.True);

        var settings = _svc.GetSettings(GUILD_ID);
        Assert.That(settings, Is.Not.Null);
        Assert.That(settings!.ChannelId, Is.EqualTo(999));
        Assert.That(settings.Threshold, Is.EqualTo(7));

        // the old messages stay in the old channel, the bot only stops tracking them
        Assert.That(await uow2.GetTable<StarboardMessage>()
                              .Where(x => x.GuildId == GUILD_ID)
                              .CountAsyncLinqToDB(),
            Is.Zero);

        // the board is built again from the starred messages, so the ones which are gone are dropped
        Assert.That(await uow2.GetTable<StarboardEntry>()
                              .Where(x => x.GuildId == GUILD_ID)
                              .CountAsyncLinqToDB(),
            Is.Zero);
    }

    [Test]
    public async Task Settings_NeedAChannelPersistAndResetRemovesEverything()
    {
        var custom = Emote.Parse("<:nadeko:123456789012345678>");
        Assert.That(StarboardService.EmoteEquals(custom, Emote.Parse("<a:renamed:123456789012345678>")), Is.True);
        Assert.That(StarboardService.EmoteEquals(custom, Emote.Parse("<:other:987654321098765432>")), Is.False);
        Assert.That(StarboardService.EmoteEquals(custom, new Emoji("⭐")), Is.False);

        var footer = new EmbedBuilder().WithFooter("⭐ 12 | 987654321098765432").Build();
        Assert.That(StarboardService.StarCountOf(footer), Is.EqualTo(12));
        Assert.That(StarboardService.TryGetEntryId(footer), Is.EqualTo(987654321098765432UL));
        Assert.That(StarboardService.TryGetEntryId(new EmbedBuilder().WithFooter("something else").Build()), Is.Null);

        Assert.That(await _svc.SetThresholdAsync(GUILD_ID, 5), Is.False);
        Assert.That(await _svc.SetEmoteAsync(GUILD_ID, "⭐"), Is.False);
        Assert.That(await _svc.ToggleAsync(GUILD_ID), Is.Null);
        Assert.That(await _svc.ToggleSelfStarAsync(GUILD_ID), Is.Null);

        await _svc.SetChannelAsync(GUILD_ID, CHANNEL_ID);

        Assert.That(await _svc.SetEmoteAsync(GUILD_ID, "definitely not an emote"), Is.False);
        Assert.That(_svc.GetSettings(GUILD_ID)!.EmoteText, Is.EqualTo(StarboardConsts.DEFAULT_EMOTE));

        Assert.That(await _svc.SetEmoteAsync(GUILD_ID, "<:blobstar:123456789012345678>"), Is.True);

        var settings = _svc.GetSettings(GUILD_ID)!;
        Assert.That(settings.Emote, Is.TypeOf<Emote>());
        Assert.That(((Emote)settings.Emote).Id, Is.EqualTo(123456789012345678UL));

        // custom emotes do not render in a footer, so the name is used there
        Assert.That(settings.FooterEmote, Is.EqualTo("blobstar"));

        // a unicode emoji renders, so it stays unchanged
        Assert.That(await _svc.SetEmoteAsync(GUILD_ID, "🌟"), Is.True);
        Assert.That(_svc.GetSettings(GUILD_ID)!.Emote, Is.EqualTo(new Emoji("🌟")));
        Assert.That(_svc.GetSettings(GUILD_ID)!.FooterEmote, Is.EqualTo("🌟"));

        await _svc.SetIgnoredChannelsAsync(GUILD_ID, [1, 2, 3]);
        Assert.That(_svc.GetIgnoredChannels(GUILD_ID), Is.EquivalentTo(new ulong[] { 1, 2, 3 }));

        await _svc.SetIgnoredChannelsAsync(GUILD_ID, [3, 4]);
        Assert.That(_svc.GetIgnoredChannels(GUILD_ID), Is.EquivalentTo(new ulong[] { 3, 4 }));

        await using (var uow = _db.GetDbContext())
        {
            Assert.That(await uow.GetTable<StarboardIgnoredChannel>().CountAsyncLinqToDB(), Is.EqualTo(2));
        }

        await _svc.SetIgnoredChannelsAsync(GUILD_ID, []);
        Assert.That(_svc.GetIgnoredChannels(GUILD_ID), Is.Empty);

        await using (var uow = _db.GetDbContext())
        {
            Assert.That(await uow.GetTable<StarboardIgnoredChannel>().CountAsyncLinqToDB(), Is.Zero);
        }

        Assert.That(await _svc.ToggleAsync(GUILD_ID), Is.False);
        Assert.That(await _svc.ToggleSelfStarAsync(GUILD_ID), Is.True);
        Assert.That(await _svc.ToggleAllowBotsAsync(GUILD_ID), Is.True);
        await _svc.SetIgnoredChannelsAsync(GUILD_ID, [777]);

        await using (var uow = _db.GetDbContext())
        {
            var row = await uow.GetTable<StarboardConfig>().FirstAsyncLinqToDB();
            Assert.That(row.IsEnabled, Is.False);
            Assert.That(row.AllowSelfStar, Is.True);
            Assert.That(row.AllowBots, Is.True);

            await uow.GetTable<StarboardEntry>()
                     .InsertAsync(() => new()
                     {
                         GuildId = GUILD_ID,
                         ChannelId = CHANNEL_ID,
                         MessageId = 1,
                         StarCount = 5,
                         Position = 0
                     });
        }

        Assert.That(await _svc.ResetAsync(GUILD_ID), Is.True);
        Assert.That(_svc.GetSettings(GUILD_ID), Is.Null);
        Assert.That(_svc.GetIgnoredChannels(GUILD_ID), Is.Empty);

        await using var uow2 = _db.GetDbContext();
        Assert.That(await uow2.GetTable<StarboardConfig>().CountAsyncLinqToDB(), Is.Zero);
        Assert.That(await uow2.GetTable<StarboardEntry>().CountAsyncLinqToDB(), Is.Zero);
        Assert.That(await uow2.GetTable<StarboardIgnoredChannel>().CountAsyncLinqToDB(), Is.Zero);
    }

    [Test]
    public async Task Limit_DropsLowestRankedEntries_AndKeepsPositionsDense()
    {
        Assert.That(PositionRange.Of(7, 3).Union(PositionRange.Of(11, 9)), Is.EqualTo(PositionRange.Of(11, 3)));
        Assert.That(PositionRange.None.Union(PositionRange.Of(7, 3)), Is.EqualTo(PositionRange.Of(7, 3)));

        await _svc.SetChannelAsync(GUILD_ID, CHANNEL_ID);
        await SeedEntriesAsync(30);

        Assert.That(await _svc.SetLimitAsync(GUILD_ID, 25), Is.True);

        await using (var uow = _db.GetDbContext())
        {
            var positions = await uow.GetTable<StarboardEntry>()
                                     .Where(x => x.GuildId == GUILD_ID)
                                     .OrderBy(x => x.Position)
                                     .Select(x => x.Position)
                                     .ToListAsyncLinqToDB();

            Assert.That(positions, Has.Count.EqualTo(25));
            Assert.That(positions[0], Is.Zero);
            Assert.That(positions[^1], Is.EqualTo(24));

            // the partial last message still holds the surviving entries of its slot range
            Assert.That(StarboardService.MessageCountFor(positions.Count), Is.EqualTo(3));
        }

        Assert.That(await _svc.SetLimitAsync(GUILD_ID, 10), Is.True);
        Assert.That(_svc.GetSettings(GUILD_ID)!.Limit, Is.EqualTo(10));

        await using var uow2 = _db.GetDbContext();
        var remaining = await uow2.GetTable<StarboardEntry>()
                                  .Where(x => x.GuildId == GUILD_ID)
                                  .OrderBy(x => x.Position)
                                  .ToListAsyncLinqToDB();

        Assert.That(remaining, Has.Count.EqualTo(10));

        // the entries which survive are the most starred ones
        Assert.That(remaining[0].StarCount, Is.EqualTo(100));
        Assert.That(remaining[^1].StarCount, Is.EqualTo(91));

        // posts stay until a full star below the threshold, and the threshold never goes under one
        Assert.That(StarboardService.RemoveThreshold(1), Is.EqualTo(1));
        Assert.That(StarboardService.RemoveThreshold(3), Is.EqualTo(2));
    }

    [Test]
    public void DirtyMessages_CollapseBursts_AndDrainLeavesNothingBehind()
    {
        // a thousand people reacting to the same message is still one unit of work
        for (var i = 0; i < 1000; i++)
            _svc.MarkDirty(500, GUILD_ID, CHANNEL_ID);

        _svc.MarkDirty(501, GUILD_ID, CHANNEL_ID);

        Assert.That(_svc.DirtyCount, Is.EqualTo(2));

        var batch = _svc.DrainDirty();

        Assert.That(batch, Has.Count.EqualTo(2));
        Assert.That(batch[500], Is.EqualTo(new DirtyMessage(GUILD_ID, CHANNEL_ID)));

        // the drain swaps in a fresh dictionary, so the same work is never done twice
        Assert.That(_svc.DirtyCount, Is.Zero);

        // reactions which arrive after the drain belong to the next tick
        _svc.MarkDirty(502, GUILD_ID, CHANNEL_ID);
        Assert.That(_svc.DirtyCount, Is.EqualTo(1));
        Assert.That(batch, Has.Count.EqualTo(2));
    }

    private async Task SeedEntriesAsync(int count)
    {
        await using var uow = _db.GetDbContext();

        for (var i = 0; i < count; i++)
        {
            var pos = i;
            await uow.GetTable<StarboardEntry>()
                     .InsertAsync(() => new()
                     {
                         GuildId = GUILD_ID,
                         ChannelId = CHANNEL_ID,
                         MessageId = (ulong)(1000 + pos),
                         StarCount = 100 - pos,
                         Position = pos
                     });
        }
    }
}
