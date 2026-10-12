using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Xp.Services;
using NadekoBot.Tests.Waifu;
using NUnit.Framework;

namespace NadekoBot.Tests.Xp;

public class ClubXpAccumulationTests
{
    private const long XP_PER_TICK = 3;

    private TestDbService _db = null!;

    [SetUp]
    public void Setup()
        => _db = new();

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    [Test]
    public async Task ClubXp_IsAFixedRatePerTick_AndIgnoresManualGrants()
    {
        var alpha = await SeedClubAsync("alpha");
        var beta = await SeedClubAsync("beta");
        var granted = await SeedClubAsync("granted", 100);

        await SeedUserAsync(1, alpha);
        await SeedUserAsync(2, alpha);
        await SeedUserAsync(3, beta);
        await SeedUserAsync(4, null);
        await SeedUserAsync(6, granted);
        await SeedUserAsync(7, granted);
        await SeedUserAsync(8, granted);

        // user 5 has no DiscordUser row, which is the normal state for most xp gainers
        // .xpadd takes any amount, including negative ones
        await ApplyBatchAsync((1, 3, true), (2, 1000, true), (3, 500, true), (4, 100, true), (5, 1000, true),
            (6, 500_000, false), (7, -500_000, false), (8, 3, true));

        var xps = await GetClubXpAsync();

        // a guild that sets .xprate to 1000 must not outweigh a guild on the default rate
        Assert.That(xps[alpha], Is.EqualTo(2 * XP_PER_TICK));
        Assert.That(xps[beta], Is.EqualTo(XP_PER_TICK));
        Assert.That(xps[granted], Is.EqualTo(100 + XP_PER_TICK));
    }

    [Test]
    public void QueuedGains_InOneFlushWindowAddUp()
    {
        var chat = new XpQueueEntry(1, 1, XP_PER_TICK, 9, true);
        var grant = new XpQueueEntry(1, 1, 500, 0, false);

        // a .xpadd and a chat message in the same window both count, in either order
        Assert.That(XpQueueEntry.Merge(chat, grant), Is.EqualTo(new XpQueueEntry(1, 1, 500 + XP_PER_TICK, 9, true)));
        Assert.That(XpQueueEntry.Merge(grant, chat), Is.EqualTo(new XpQueueEntry(1, 1, 500 + XP_PER_TICK, 9, true)));
    }

    [Test]
    public async Task ClubXp_AddsOncePerMemberAcrossServers_AndLeavesIdleClubsAlone()
    {
        var active = await SeedClubAsync("active", 1000);
        var idle = await SeedClubAsync("idle", 250);

        await SeedUserAsync(1, active);
        await SeedUserAsync(2, idle);

        await ApplyGuildBatchAsync([(1, 1, 3, true), (2, 1, 3, true)]);
        await ApplyGuildBatchAsync([(1, 1, 3, true), (2, 1, 3, true)]);

        var xps = await GetClubXpAsync();

        Assert.That(xps[active], Is.EqualTo(1000 + (2 * XP_PER_TICK)));
        Assert.That(xps[idle], Is.EqualTo(250));
    }

    private Task ApplyBatchAsync(params (ulong UserId, long Xp, bool CountsForClub)[] gains)
        => ApplyGuildBatchAsync(gains.Select(static x => ((ulong)1, x.UserId, x.Xp, x.CountsForClub)).ToArray());

    private async Task ApplyGuildBatchAsync((ulong GuildId, ulong UserId, long Xp, bool CountsForClub)[] gains)
    {
        await using var ctx = _db.GetDbContext();
        await using var lctx = ctx.CreateLinqToDBConnection();

        const string TEMP_TABLE_NAME = "xptemp_test";
        await using var batchTable = await lctx.CreateTempTableAsync<UserXpBatch>(TEMP_TABLE_NAME);

        await batchTable.BulkCopyAsync(gains.Select(static x => new UserXpBatch
        {
            GuildId = x.GuildId,
            UserId = x.UserId,
            XpToGain = x.Xp,
            CountsForClub = x.CountsForClub
        }));

        await XpService.AddClubXpAsync(lctx, TEMP_TABLE_NAME);
    }

    private async Task<int> SeedClubAsync(string name, long xp = 0)
    {
        await using var uow = _db.GetDbContext();
        return await uow.GetTable<ClubInfo>()
                        .InsertWithInt32IdentityAsync(() => new()
                        {
                            Name = name,
                            Description = "",
                            ImageUrl = "",
                            BannerUrl = "",
                            Xp = xp
                        });
    }

    private async Task SeedUserAsync(ulong userId, int? clubId)
    {
        await using var uow = _db.GetDbContext();
        await uow.GetTable<DiscordUser>()
                 .InsertAsync(() => new()
                 {
                     UserId = userId,
                     Username = "u" + userId,
                     ClubId = clubId
                 });
    }

    private async Task<Dictionary<int, long>> GetClubXpAsync()
    {
        await using var uow = _db.GetDbContext();
        var clubs = await uow.GetTable<ClubInfo>()
                             .Select(x => new
                             {
                                 x.Id,
                                 x.Xp
                             })
                             .ToListAsyncLinqToDB();

        return clubs.ToDictionary(static x => x.Id, static x => x.Xp);
    }
}
