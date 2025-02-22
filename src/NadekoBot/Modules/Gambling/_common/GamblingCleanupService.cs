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
    
    public async Task DeleteWaifus()
    {
        await using var ctx = _db.GetDbContext();
        await ctx.GetTable<WaifuItem>().DeleteAsync();
        await ctx.GetTable<WaifuUpdate>().DeleteAsync();
        await ctx.GetTable<WaifuInfo>().DeleteAsync();
    }

    public async Task DeleteWaifu(ulong userId)
    {
        await using var ctx = _db.GetDbContext();
        await ctx.GetTable<WaifuUpdate>()
            .Where(x => x.User.UserId == userId)
            .DeleteAsync();
        await ctx.GetTable<WaifuItem>()
            .Where(x => x.WaifuInfo.Waifu.UserId == userId)
            .DeleteAsync();
        await ctx.GetTable<WaifuInfo>()
            .Where(x => x.Claimer.UserId == userId)
            .UpdateAsync(old => new WaifuInfo()
            {
                ClaimerId = null,
            });
        await ctx.GetTable<WaifuInfo>()
            .Where(x => x.Waifu.UserId == userId)
            .DeleteAsync();
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