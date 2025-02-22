#nullable disable
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Db.Models;

namespace NadekoBot.Db;

public static class CurrencyTransactionExtensions
{
    public static Task<List<CurrencyTransaction>> GetPageFor(
        this DbSet<CurrencyTransaction> set,
        ulong userId,
        int page)
        => set.ToLinqToDBTable()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.DateAdded)
            .Skip(15 * page)
            .Take(15)
            .ToListAsyncLinqToDB();
}