using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Db.Models;

namespace NadekoBot.Services.Currency;

public class DefaultWallet : IWallet
{
    private readonly DbService _db;
    public ulong UserId { get; }

    public DefaultWallet(ulong userId, DbService db)
    {
        UserId = userId;
        _db = db;
    }

    public async Task<long> GetBalance()
    {
        await using var ctx = _db.GetDbContext();
        var userId = UserId;
        return await ctx
                     .GetTable<DiscordUser>()
                     .Where(x => x.UserId == userId)
                     .Select(x => x.CurrencyAmount)
                     .FirstOrDefaultAsync();
    }

    public async Task<bool> Take(long amount, TxData? txData)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount to take must be non negative.");

        await using var ctx = _db.GetDbContext();

        var userId = UserId;
        var changed = await ctx
                            .GetTable<DiscordUser>()
                            .Where(x => x.UserId == userId && x.CurrencyAmount >= amount)
                            .UpdateAsync(x => new()
                            {
                                CurrencyAmount = x.CurrencyAmount - amount
                            });

        if (changed == 0)
            return false;

        if (txData is not null)
        {
            await ctx
                  .GetTable<CurrencyTransaction>()
                  .InsertAsync(() => new()
                  {
                      Amount = -amount,
                      Note = txData.Note,
                      UserId = userId,
                      Type = txData.Type,
                      Extra = txData.Extra,
                      OtherId = txData.OtherId,
                      DateAdded = DateTime.UtcNow
                  });
        }

        return true;
    }

    public async Task Add(long amount, TxData? txData)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be greater than 0.");

        await using var ctx = _db.GetDbContext();
        var userId = UserId;


        await ctx.GetTable<DiscordUser>()
                 .InsertOrUpdateAsync(() => new()
                     {
                         UserId = userId,
                         Username = "??Unknown",
                         CurrencyAmount = amount,
                     },
                     (old) => new()
                     {
                         CurrencyAmount = old.CurrencyAmount + amount
                     },
                     () => new()
                     {
                         UserId = userId
                     });

        if (txData is not null)
        {
            await ctx.GetTable<CurrencyTransaction>()
                     .InsertAsync(() => new()
                     {
                         Amount = amount,
                         UserId = userId,
                         Note = txData.Note,
                         Type = txData.Type,
                         Extra = txData.Extra,
                         OtherId = txData.OtherId,
                         DateAdded = DateTime.UtcNow
                     });
        }
    }
}