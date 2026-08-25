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
            Substitute.For<IBotStrings>());
    }

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    [Test]
    public void ParseEmote_AcceptsUnicodeAndCustom_RejectsGarbage()
    {
        Assert.That(StarboardService.ParseEmote("⭐"), Is.InstanceOf<Emoji>());
        Assert.That(StarboardService.ParseEmote("<:nadeko:123456789012345678>"), Is.InstanceOf<Emote>());
        Assert.That(StarboardService.ParseEmote("<a:spin:123456789012345678>"), Is.InstanceOf<Emote>());

        Assert.That(StarboardService.ParseEmote("not an emote"), Is.Null);
        Assert.That(StarboardService.ParseEmote(""), Is.Null);
        Assert.That(StarboardService.ParseEmote(new string('x', StarboardConsts.MAX_EMOTE_LENGTH + 1)), Is.Null);
    }

    [Test]
    public void EmoteEquals_MatchesCustomById_AndNeverAcrossKinds()
    {
        var custom = Emote.Parse("<:nadeko:123456789012345678>");
        var sameIdAnimated = Emote.Parse("<a:renamed:123456789012345678>");
        var otherCustom = Emote.Parse("<:other:987654321098765432>");

        Assert.That(StarboardService.EmoteEquals(custom, sameIdAnimated), Is.True);
        Assert.That(StarboardService.EmoteEquals(custom, otherCustom), Is.False);

        Assert.That(StarboardService.EmoteEquals(new Emoji("⭐"), new Emoji("⭐")), Is.True);
        Assert.That(StarboardService.EmoteEquals(new Emoji("⭐"), new Emoji("🌟")), Is.False);

        Assert.That(StarboardService.EmoteEquals(custom, new Emoji("⭐")), Is.False);
    }

    [Test]
    public async Task SetChannel_CreatesDefaults_AndKeepsSettingsOnRepeat()
    {
        await _svc.SetChannelAsync(GUILD_ID, CHANNEL_ID);
        await _svc.SetThresholdAsync(GUILD_ID, 7);
        await _svc.SetChannelAsync(GUILD_ID, 999);

        await using var uow = _db.GetDbContext();
        var rows = await uow.GetTable<StarboardConfig>().ToListAsyncLinqToDB();

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].ChannelId, Is.EqualTo(999));
        Assert.That(rows[0].Threshold, Is.EqualTo(7));
        Assert.That(rows[0].Emote, Is.EqualTo(StarboardConsts.DEFAULT_EMOTE));
        Assert.That(rows[0].IsEnabled, Is.True);

        var settings = _svc.GetSettings(GUILD_ID);
        Assert.That(settings, Is.Not.Null);
        Assert.That(settings!.ChannelId, Is.EqualTo(999));
        Assert.That(settings.Threshold, Is.EqualTo(7));
    }

    [Test]
    public async Task Setters_RejectUnconfiguredGuild_AndInvalidEmote()
    {
        Assert.That(await _svc.SetThresholdAsync(GUILD_ID, 5), Is.False);
        Assert.That(await _svc.SetEmoteAsync(GUILD_ID, "⭐"), Is.False);
        Assert.That(await _svc.ToggleAsync(GUILD_ID), Is.Null);
        Assert.That(await _svc.ToggleSelfStarAsync(GUILD_ID), Is.Null);

        await _svc.SetChannelAsync(GUILD_ID, CHANNEL_ID);

        Assert.That(await _svc.SetEmoteAsync(GUILD_ID, "definitely not an emote"), Is.False);
        Assert.That(_svc.GetSettings(GUILD_ID)!.EmoteText, Is.EqualTo(StarboardConsts.DEFAULT_EMOTE));

        Assert.That(await _svc.SetEmoteAsync(GUILD_ID, "🌟"), Is.True);
        Assert.That(_svc.GetSettings(GUILD_ID)!.Emote, Is.EqualTo(new Emoji("🌟")));
    }

    [Test]
    public async Task CustomEmote_IsAccepted_AndFooterUsesItsName()
    {
        await _svc.SetChannelAsync(GUILD_ID, CHANNEL_ID);

        Assert.That(await _svc.SetEmoteAsync(GUILD_ID, "<:blobstar:123456789012345678>"), Is.True);

        var settings = _svc.GetSettings(GUILD_ID)!;
        Assert.That(settings.Emote, Is.TypeOf<Emote>());
        Assert.That(((Emote)settings.Emote).Id, Is.EqualTo(123456789012345678UL));

        // custom emotes do not render in a footer, so the name is used there
        Assert.That(settings.FooterEmote, Is.EqualTo("blobstar"));

        // a unicode emoji renders, so it stays unchanged
        Assert.That(await _svc.SetEmoteAsync(GUILD_ID, "🌟"), Is.True);
        Assert.That(_svc.GetSettings(GUILD_ID)!.FooterEmote, Is.EqualTo("🌟"));
    }

    [Test]
    public async Task Toggles_FlipState_AndResetRemovesEverything()
    {
        await _svc.SetChannelAsync(GUILD_ID, CHANNEL_ID);

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
    public void RemoveThreshold_KeepsPostsUntilAFullStarBelow_AndNeverGoesUnderOne()
    {
        Assert.That(StarboardService.RemoveThreshold(1), Is.EqualTo(1));
        Assert.That(StarboardService.RemoveThreshold(3), Is.EqualTo(2));
        Assert.That(StarboardService.RemoveThreshold(StarboardConsts.MAX_THRESHOLD),
            Is.EqualTo(StarboardConsts.MAX_THRESHOLD - 1));
    }

    [Test]
    public void Layout_MapsPositionsToMessagesTenAtATime()
    {
        var perMessage = StarboardConsts.MAX_EMBEDS_PER_MESSAGE;

        Assert.That(StarboardService.MessageIndexOf(0), Is.Zero);
        Assert.That(StarboardService.MessageIndexOf(perMessage - 1), Is.Zero);
        Assert.That(StarboardService.MessageIndexOf(perMessage), Is.EqualTo(1));

        Assert.That(StarboardService.MessageCountFor(0), Is.Zero);
        Assert.That(StarboardService.MessageCountFor(1), Is.EqualTo(1));
        Assert.That(StarboardService.MessageCountFor(perMessage), Is.EqualTo(1));
        Assert.That(StarboardService.MessageCountFor(perMessage + 1), Is.EqualTo(2));

        // the example from the spec: 53 entries occupy 6 messages
        Assert.That(StarboardService.MessageCountFor(53), Is.EqualTo(6));
    }

    [Test]
    public void FooterParsers_ReadBackStarCountAndMessageId()
    {
        var embed = new EmbedBuilder().WithFooter("⭐ 12 | 987654321098765432").Build();

        Assert.That(StarboardService.StarCountOf(embed), Is.EqualTo(12));
        Assert.That(StarboardService.TryGetEntryId(embed), Is.EqualTo(987654321098765432UL));

        // a custom emote renders as its name, which contains no digits to confuse the parser
        var custom = new EmbedBuilder().WithFooter("blobstar 7 | 123").Build();
        Assert.That(StarboardService.StarCountOf(custom), Is.EqualTo(7));
        Assert.That(StarboardService.TryGetEntryId(custom), Is.EqualTo(123UL));

        var foreign = new EmbedBuilder().WithFooter("something else").Build();
        Assert.That(StarboardService.StarCountOf(foreign), Is.EqualTo(-1));
        Assert.That(StarboardService.TryGetEntryId(foreign), Is.Null);
    }

    [Test]
    public async Task Limit_DropsLowestRankedEntries()
    {
        await _svc.SetChannelAsync(GUILD_ID, CHANNEL_ID);
        await SeedEntriesAsync(30);

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
    }

    [Test]
    public async Task Limit_WhichIsNotAWholeMessage_KeepsPositionsDense()
    {
        await _svc.SetChannelAsync(GUILD_ID, CHANNEL_ID);
        await SeedEntriesAsync(30);

        Assert.That(await _svc.SetLimitAsync(GUILD_ID, 25), Is.True);

        await using var uow = _db.GetDbContext();
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

    [Test]
    public async Task ChannelSwitch_ForgetsOldBoardMessages_AndDropsUnfetchableEntries()
    {
        await _svc.SetChannelAsync(GUILD_ID, CHANNEL_ID);
        await SeedEntriesAsync(12);

        await using (var uow = _db.GetDbContext())
        {
            await uow.GetTable<StarboardMessage>()
                     .InsertAsync(() => new() { GuildId = GUILD_ID, Index = 0, MessageId = 5001 });
            await uow.GetTable<StarboardMessage>()
                     .InsertAsync(() => new() { GuildId = GUILD_ID, Index = 1, MessageId = 5002 });
        }

        await _svc.SetChannelAsync(GUILD_ID, 777);

        Assert.That(_svc.GetSettings(GUILD_ID)!.ChannelId, Is.EqualTo(777UL));

        await using var uow2 = _db.GetDbContext();

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

    [Test]
    public void PositionRange_MergesTouchedSpans_SoOneBatchRendersOnce()
    {
        Assert.That(PositionRange.None.IsSome, Is.False);

        var a = PositionRange.Of(7, 3);
        Assert.That(a.From, Is.EqualTo(3));
        Assert.That(a.To, Is.EqualTo(7));

        // merging with nothing keeps the span, in both directions
        Assert.That(a.Union(PositionRange.None), Is.EqualTo(a));
        Assert.That(PositionRange.None.Union(a), Is.EqualTo(a));

        var merged = a.Union(PositionRange.Of(11, 9));
        Assert.That(merged.From, Is.EqualTo(3));
        Assert.That(merged.To, Is.EqualTo(11));
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

    [Test]
    public async Task IgnoredChannels_ReplaceWholeSet_AndClearOnEmpty()
    {
        await _svc.SetChannelAsync(GUILD_ID, CHANNEL_ID);

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

        await using var uow2 = _db.GetDbContext();
        Assert.That(await uow2.GetTable<StarboardIgnoredChannel>().CountAsyncLinqToDB(), Is.Zero);
    }
}
