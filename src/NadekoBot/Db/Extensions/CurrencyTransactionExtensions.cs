#nullable disable
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Db.Models;

namespace NadekoBot.Db;

public static class CurrencyTransactionExtensions
{
    public static async Task<IReadOnlyCollection<CurrencyTransaction>> GetPageFor(
        this DbSet<CurrencyTransaction> set,
        ulong userId,
        int page)
    {
        var items = await set.ToLinqToDBTable()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.DateAdded)
            .Skip(15 * page)
            .Take(15)
            .ToListAsyncLinqToDB();
        
        return items;
    }
    
    public static async Task<int> GetCountFor(this DbSet<CurrencyTransaction> set, ulong userId)
        => await set.ToLinqToDBTable()
            .Where(x => x.UserId == userId)
            .CountAsyncLinqToDB();
}