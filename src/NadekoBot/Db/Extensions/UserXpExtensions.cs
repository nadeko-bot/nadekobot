using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Db.Models;

namespace NadekoBot.Db;

public static class UserXpExtensions
{
    public static async Task<UserXpStats?> GetGuildUserXp(this ITable<UserXpStats> table, ulong guildId, ulong userId)
        => await table.FirstOrDefaultAsyncLinqToDB(x => x.GuildId == guildId && x.UserId == userId);
    
    public static UserXpStats GetOrCreateUserXpStats(this DbContext ctx, ulong guildId, ulong userId)
    {
        var usr = ctx.Set<UserXpStats>().FirstOrDefault(x => x.UserId == userId && x.GuildId == guildId);

        if (usr is null)
        {
            ctx.Add(usr = new()
            {
                Xp = 0,
                UserId = userId,
                GuildId = guildId
            });
        }

        return usr;
    }

    public static async Task<List<UserXpStats>> GetTopUserXps(this DbSet<UserXpStats> xps, ulong guildId, int count)
        => await xps.ToLinqToDBTable()
                    .Where(x => x.GuildId == guildId)
                    .OrderByDescending(x => x.Xp)
                    .Take(count)
                    .ToListAsyncLinqToDB();

    public static async Task<int> GetUserGuildRanking(this DbSet<UserXpStats> xps, ulong userId, ulong guildId)
        => await xps.ToLinqToDBTable()
                    .Where(x => x.GuildId == guildId
                                && x.Xp
                                > xps.AsQueryable()
                                     .Where(y => y.UserId == userId && y.GuildId == guildId)
                                     .Select(y => y.Xp)
                                     .FirstOrDefault())
                    .CountAsyncLinqToDB()
           + 1;

    public static void ResetGuildXp(this DbSet<UserXpStats> xps, ulong guildId)
        => xps.Delete(x => x.GuildId == guildId);

    public static async Task<LevelStats> GetLevelDataFor(this ITable<UserXpStats> userXp, ulong guildId, ulong userId)
        => await userXp
                 .Where(x => x.GuildId == guildId && x.UserId == userId)
                 .FirstOrDefaultAsyncLinqToDB() is UserXpStats uxs
            ? new(uxs.Xp)
            : new(0);
}