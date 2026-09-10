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
    public async Task ClubXp_IsAFixedRatePerTick_NoMatterHowMuchXpTheMemberGained()
    {
        var alpha = await SeedClubAsync("alpha");
        var beta = await SeedClubAsync("beta");

        await SeedUserAsync(1, alpha);
        await SeedUserAsync(2, alpha);
        await SeedUserAsync(3, beta);
        await SeedUserAsync(4, null);

        // user 5 has no DiscordUser row, which is the normal state for most xp gainers
        await ApplyBatchAsync((1, 3, true), (2, 1000, true), (3, 500, true), (4, 100, true), (5, 1000, true));

        var xps = await GetClubXpAsync();

        // a guild that sets .xprate to 1000 must not outweigh a guild on the default rate
        Assert.That(xps[alpha], Is.EqualTo(2 * XP_PER_TICK));
        Assert.That(xps[beta], Is.EqualTo(XP_PER_TICK));
    }

    [Test]
    public async Task ClubXp_IgnoresManualGrants()
    {
        var club = await SeedClubAsync("club", 100);
        await SeedUserAsync(1, club);
        await SeedUserAsync(2, club);
        await SeedUserAsync(3, club);

        // .xpadd takes any amount, including negative ones
        await ApplyBatchAsync((1, 500_000, false), (2, -500_000, false), (3, 3, true));

        var xps = await GetClubXpAsync();

        Assert.That(xps[club], Is.EqualTo(100 + XP_PER_TICK));
    }

    [Test]
    public async Task ClubXp_AddsToTheExistingTotalAndLeavesIdleClubsAlone()
    {
        var active = await SeedClubAsync("active", 1000);
        var idle = await SeedClubAsync("idle", 250);

        await SeedUserAsync(1, active);
        await SeedUserAsync(2, idle);

        await ApplyBatchAsync((1, 3, true));
        await ApplyBatchAsync((1, 3, true));

        var xps = await GetClubXpAsync();

        Assert.That(xps[active], Is.EqualTo(1000 + (2 * XP_PER_TICK)));
        Assert.That(xps[idle], Is.EqualTo(250));
    }

    private async Task ApplyBatchAsync(params (ulong UserId, long Xp, bool CountsForClub)[] gains)
    {
        await using var ctx = _db.GetDbContext();
        await using var lctx = ctx.CreateLinqToDBConnection();

        const string TEMP_TABLE_NAME = "xptemp_test";
        await using var batchTable = await lctx.CreateTempTableAsync<UserXpBatch>(TEMP_TABLE_NAME);

        await batchTable.BulkCopyAsync(gains.Select(static x => new UserXpBatch
        {
            GuildId = 1,
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
