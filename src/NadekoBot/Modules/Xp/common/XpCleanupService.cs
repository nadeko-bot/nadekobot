using LinqToDB;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Xp;

public sealed class XpCleanupService : IXpCleanupService, INService
{
    private readonly DbService _db;

    public XpCleanupService(DbService db)
    {
        _db = db;
    }

    public async Task DeleteXp()
    {
        await using var uow = _db.GetDbContext();
        await uow.Set<DiscordUser>().UpdateAsync(_ => new DiscordUser()
        {
            ClubId = null,
            // IsClubAdmin = false,
            TotalXp = 0
        });

        await uow.Set<UserXpStats>().DeleteAsync();
        await uow.Set<ClubApplicants>().DeleteAsync();
        await uow.Set<ClubBans>().DeleteAsync();
        await uow.Set<ClubInfo>().DeleteAsync();
        await uow.SaveChangesAsync();
    }
}