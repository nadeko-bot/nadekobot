#nullable disable
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Db.Models;

namespace NadekoBot.Db;

public static class DiscordUserExtensions
{
    public static Task<DiscordUser> GetByUserIdAsync(
        this IQueryable<DiscordUser> set,
        ulong userId)
        => set.FirstOrDefaultAsyncLinqToDB(x => x.UserId == userId);

    public static void EnsureUserCreated(
        this DbContext ctx,
        ulong userId,
        string username,
        string discrim,
        string avatarId)
        => ctx.GetTable<DiscordUser>()
              .InsertOrUpdate(
                  () => new()
                  {
                      UserId = userId,
                      Username = username,
                      AvatarId = avatarId,
                      TotalXp = 0,
                      CurrencyAmount = 0
                  },
                  old => new()
                  {
                      Username = username,
                      AvatarId = avatarId
                  },
                  () => new()
                  {
                      UserId = userId
                  });

    public static Task EnsureUserCreatedAsync(
        this DbContext ctx,
        ulong userId)
        => ctx.GetTable<DiscordUser>()
              .InsertOrUpdateAsync(
                  () => new()
                  {
                      UserId = userId,
                      Username = "??Unknown",
                      AvatarId = string.Empty,
                      TotalXp = 0,
                      CurrencyAmount = 0
                  },
                  old => new()
                  {
                  },
                  () => new()
                  {
                      UserId = userId
                  });

    //temp is only used in updatecurrencystate, so that i don't overwrite real usernames/discrims with Unknown
    public static DiscordUser GetOrCreateUser(
        this DbContext ctx,
        ulong userId,
        string username,
        string discrim,
        string avatarId,
        Func<IQueryable<DiscordUser>, IQueryable<DiscordUser>> includes = null)
    {
        ctx.EnsureUserCreated(userId, username, discrim, avatarId);

        IQueryable<DiscordUser> queryable = ctx.Set<DiscordUser>();
        if (includes is not null)
            queryable = includes(queryable);
        return queryable.First(u => u.UserId == userId);
    }


    public static int GetUserGlobalRank(this DbSet<DiscordUser> users, ulong id)
        => users.AsQueryable()
                .Where(x => x.TotalXp
                            > users.AsQueryable().Where(y => y.UserId == id).Select(y => y.TotalXp).FirstOrDefault())
                .Count()
           + 1;
    
    public static Task<List<DiscordUser>> GetTopRichest(
        this DbSet<DiscordUser> users,
        ulong botId,
        int page = 0,
        int perPage = 9)
        => users.AsQueryable()
                .Where(c => c.CurrencyAmount > 0 && botId != c.UserId)
                .OrderByDescending(c => c.CurrencyAmount)
                .Skip(page * perPage)
                .Take(perPage)
                .ToListAsyncLinqToDB();

    public static async Task<long> GetUserCurrencyAsync(this DbSet<DiscordUser> users, ulong userId)
        => (await users.FirstOrDefaultAsyncLinqToDB(x => x.UserId == userId))?.CurrencyAmount ?? 0;

    public static void RemoveFromMany(this DbSet<DiscordUser> users, IEnumerable<ulong> ids)
    {
        var items = users.AsQueryable().Where(x => ids.Contains(x.UserId));
        foreach (var item in items)
            item.CurrencyAmount = 0;
    }

    public static decimal GetTotalCurrency(this DbSet<DiscordUser> users)
        => users.Sum((Func<DiscordUser, decimal>)(x => x.CurrencyAmount));

    public static decimal GetTopOnePercentCurrency(this DbSet<DiscordUser> users, ulong botId)
        => users.AsQueryable()
                .Where(x => x.UserId != botId)
                .OrderByDescending(x => x.CurrencyAmount)
                .Take(users.Count() / 100 == 0 ? 1 : users.Count() / 100)
                .Sum(x => x.CurrencyAmount);
}