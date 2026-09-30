using System.Linq;
using System.Threading.Tasks;
using Discord.WebSocket;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common;
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

    [SetUp]
    public void Setup()
    {
        _db = new TestDbService();

        var client = Substitute.For<DiscordSocketClient>();
        var creds = Substitute.For<IBotCreds>();
        creds.TotalShards.Returns(1);

        _svc = new GuildTimezoneService(_db,
            Substitute.For<IReplacementPatternStore>(),
            new ShardData(client, creds),
            client);
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
        Assert.That(_svc.GetTimeZoneOrDefault(GUILD_ID)?.Id, Is.EqualTo(tokyo.Id));
        Assert.That(await StoredIdAsync(), Is.EqualTo(tokyo.Id));

        await _svc.SetTimeZoneAsync(GUILD_ID, null!);
        Assert.That(_svc.GetTimeZoneOrDefault(GUILD_ID), Is.Null);
        Assert.That(await StoredIdAsync(), Is.Null);

        await using var ctx = _db.GetDbContext();
        Assert.That(await ctx.GetTable<GuildConfig>().CountAsyncLinqToDB(x => x.GuildId == GUILD_ID), Is.EqualTo(1));
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
