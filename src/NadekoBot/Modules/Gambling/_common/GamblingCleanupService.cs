using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Gambling;

public class GamblingCleanupService : IGamblingCleanupService, INService
{
    private readonly DbService _db;

    public GamblingCleanupService(DbService db)
    {
        _db = db;
    }

    public async Task DeleteCurrency()
    {
        await using var ctx = _db.GetDbContext();
        await ctx.GetTable<DiscordUser>().UpdateAsync(_ => new DiscordUser()
        {
            CurrencyAmount = 0
        });

        await ctx.GetTable<CurrencyTransaction>().DeleteAsync();
        await ctx.GetTable<PlantedCurrency>().DeleteAsync();
        await ctx.GetTable<BankUser>().DeleteAsync();
    }
}
