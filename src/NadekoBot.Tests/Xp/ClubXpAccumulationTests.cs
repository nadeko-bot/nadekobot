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
    private TestDbService _db = null!;

    [SetUp]
    public void Setup()
        => _db = new();

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    [Test]
    public async Task ClubXp_OnlyCountsItsOwnMembers()
    {
        var alpha = await SeedClubAsync("alpha");
        var beta = await SeedClubAsync("beta");

        await SeedUserAsync(1, alpha);
        await SeedUserAsync(2, alpha);
        await SeedUserAsync(3, beta);
        await SeedUserAsync(4, null);

        // user 5 has no DiscordUser row at all, which is the normal state for most xp gainers
        await ApplyBatchAsync((1, 10), (2, 5), (3, 7), (4, 100), (5, 1000));

        var xps = await GetClubXpAsync();

        Assert.That(xps[alpha], Is.EqualTo(15));
        Assert.That(xps[beta], Is.EqualTo(7));
    }

    [Test]
    public async Task ClubXp_AddsToTheExistingTotalAndLeavesIdleClubsAlone()
    {
        var active = await SeedClubAsync("active", 1000);
        var idle = await SeedClubAsync("idle", 250);

        await SeedUserAsync(1, active);
        await SeedUserAsync(2, idle);

        await ApplyBatchAsync((1, 30));
        await ApplyBatchAsync((1, 12));

        var xps = await GetClubXpAsync();

        Assert.That(xps[active], Is.EqualTo(1042));
        Assert.That(xps[idle], Is.EqualTo(250));
    }

    private async Task ApplyBatchAsync(params (ulong UserId, long Xp)[] gains)
    {
        await using var ctx = _db.GetDbContext();
        await using var lctx = ctx.CreateLinqToDBConnection();

        const string TEMP_TABLE_NAME = "xptemp_test";
        await using var batchTable = await lctx.CreateTempTableAsync<UserXpBatch>(TEMP_TABLE_NAME);

        await batchTable.BulkCopyAsync(gains.Select(static x => new UserXpBatch
        {
            GuildId = 1,
            UserId = x.UserId,
            XpToGain = x.Xp
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
