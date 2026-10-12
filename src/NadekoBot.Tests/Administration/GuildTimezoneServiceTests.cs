using System;
using System.Linq;
using System.Threading.Tasks;
using Discord.WebSocket;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Nadeko.Common;
using NadekoBot.Common;
using NadekoBot.Common.Configs;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Administration.Services;
using NadekoBot.Services;
using NadekoBot.Tests.Waifu;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Administration;

public class GuildTimezoneServiceTests
{
    private const ulong GUILD_ID = 111;

    private TestDbService _db = null!;
    private GuildTimezoneService _svc = null!;
    private BotConfigService _bcs = null!;

    [SetUp]
    public void Setup()
    {
        _db = new TestDbService();

        var client = Substitute.For<DiscordSocketClient>();
        var creds = Substitute.For<IBotCreds>();
        creds.TotalShards.Returns(1);
        _bcs = new BotConfigService(new YamlSeria(), Substitute.For<IPubSub>(), new BotPolicy(false));

        _svc = new GuildTimezoneService(_db,
            Substitute.For<IReplacementPatternStore>(),
            new ShardData(client, creds),
            client,
            _bcs);
    }

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    [Test]
    public void TryFindTimeZone_IgnoresCaseAndAcceptsCities()
    {
        var berlin = GuildTimezoneService.TryFindTimeZone("Europe/Berlin");
        Assert.That(berlin, Is.Not.Null);

        Assert.That(GuildTimezoneService.TryFindTimeZone(" europe/berlin ")?.Id, Is.EqualTo(berlin!.Id));
        Assert.That(GuildTimezoneService.TryFindTimeZone("berlin")?.Id, Is.EqualTo(berlin.Id));
        Assert.That(GuildTimezoneService.TryFindTimeZone("new york")?.Id, Is.EqualTo("America/New_York"));

        Assert.That(GuildTimezoneService.TryFindTimeZone("Nowhere/Atlantis"), Is.Null);
        Assert.That(GuildTimezoneService.TryFindTimeZone("   "), Is.Null);
    }

    [Test]
    public async Task SetTimeZone_CreatesUpdatesAndClearsTheGuildSetting()
    {
        var berlin = GuildTimezoneService.TryFindTimeZone("Europe/Berlin")!;
        var tokyo = GuildTimezoneService.TryFindTimeZone("Asia/Tokyo")!;

        await _svc.SetTimeZoneAsync(GUILD_ID, berlin);
        await _svc.SetTimeZoneAsync(GUILD_ID, tokyo);
        Assert.That(_svc.GetGuildTimeZone(GUILD_ID)?.Id, Is.EqualTo(tokyo.Id));
        Assert.That(_svc.GetTimeZoneOrDefault(GUILD_ID)?.Id, Is.EqualTo(tokyo.Id));
        Assert.That(await StoredIdAsync(), Is.EqualTo(tokyo.Id));

        await _svc.SetTimeZoneAsync(GUILD_ID, null!);
        Assert.That(_svc.GetGuildTimeZone(GUILD_ID), Is.Null);
        Assert.That(await StoredIdAsync(), Is.Null);

        await using var ctx = _db.GetDbContext();
        Assert.That(await ctx.GetTable<GuildConfig>().CountAsyncLinqToDB(x => x.GuildId == GUILD_ID), Is.EqualTo(1));
    }

    [Test]
    public async Task GuildZone_ThenBotDefault_ThenUtc()
    {
        var berlin = GuildTimezoneService.TryFindTimeZone("Europe/Berlin")!;
        var tokyo = GuildTimezoneService.TryFindTimeZone("Asia/Tokyo")!;
        try
        {
            Assert.That(_svc.GetTimeZoneOrUtc(GUILD_ID), Is.SameAs(TimeZoneInfo.Utc));

            _bcs.ModifyConfig(c => c.DefaultTimezone = berlin.Id);
            Assert.That(_svc.GetTimeZoneOrUtc(GUILD_ID).Id, Is.EqualTo(berlin.Id));

            await _svc.SetTimeZoneAsync(GUILD_ID, tokyo);
            Assert.That(_svc.GetTimeZoneOrUtc(GUILD_ID).Id, Is.EqualTo(tokyo.Id));

            _bcs.ModifyConfig(c => c.DefaultTimezone = "Nowhere/Atlantis");
            Assert.That(_svc.GetTimeZoneOrUtc(GUILD_ID + 1), Is.SameAs(TimeZoneInfo.Utc));
        }
        finally
        {
            _bcs.ModifyConfig(c => c.DefaultTimezone = null);
        }
    }

    [Test]
    public void TimeZoneIdParser_AcceptsZonesAndClearsOnEmpty()
    {
        Assert.That(ConfigParsers.TimeZoneId(" America/New_York ", out var id), Is.True);
        Assert.That(id, Is.EqualTo("America/New_York"));
        Assert.That(ConfigParsers.TimeZoneId("berlin", out id), Is.True);
        Assert.That(id, Is.EqualTo("Europe/Berlin"));

        Assert.That(ConfigParsers.TimeZoneId("Nowhere/Atlantis", out _), Is.False);

        Assert.That(ConfigParsers.TimeZoneId("  ", out id), Is.True);
        Assert.That(id, Is.Null);
        Assert.That(ConfigPrinters.TimeZoneId(id!), Is.EqualTo("UTC"));
    }

    private async Task<string?> StoredIdAsync()
    {
        await using var ctx = _db.GetDbContext();
        return await ctx.GetTable<GuildConfig>()
            .Where(x => x.GuildId == GUILD_ID)
            .Select(x => x.TimeZoneId)
            .FirstAsyncLinqToDB();
    }
}
