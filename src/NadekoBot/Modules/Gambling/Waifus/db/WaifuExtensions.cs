#nullable disable
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Db.Models;

namespace NadekoBot.Db;

public static class WaifuExtensions
{
    public static WaifuInfo ByWaifuUserId(
        this DbSet<WaifuInfo> waifus,
        ulong userId,
        Func<DbSet<WaifuInfo>, IQueryable<WaifuInfo>> includes = null)
    {
        if (includes is null)
        {
            return waifus.Include(wi => wi.Waifu)
                         .Include(wi => wi.Affinity)
                         .Include(wi => wi.Claimer)
                         .Include(wi => wi.Items)
                         .FirstOrDefault(wi => wi.Waifu.UserId == userId);
        }

        return includes(waifus).AsQueryable().FirstOrDefault(wi => wi.Waifu.UserId == userId);
    }

    public static async Task<IReadOnlyList<WaifuLbResult>> GetTop(this DbSet<WaifuInfo> waifus, int count, int skip = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (count == 0)
            return [];

        return await waifus.Include(wi => wi.Waifu)
                           .Include(wi => wi.Affinity)
                           .Include(wi => wi.Claimer)
                           .OrderByDescending(wi => wi.Price)
                           .Skip(skip)
                           .Take(count)
                           .Select(x => new WaifuLbResult
                           {
                               Affinity = x.Affinity == null
                                   ? null
                                   : x.Affinity.Username,
                               ClaimerName =
                                   x.Claimer == null
                                       ? null
                                       : x.Claimer.Username,
                               WaifuName = x.Waifu.Username,
                               Price = x.Price
                           })
                           .ToListAsyncEF();
    }

    public static decimal GetTotalValue(this DbSet<WaifuInfo> waifus)
        => waifus.AsQueryable().Where(x => x.ClaimerId != null).Sum(x => x.Price);

    public static ulong GetWaifuUserId(this DbSet<WaifuInfo> waifus, ulong ownerId, string name)
        => waifus.AsQueryable()
                 .AsNoTracking()
                 .Where(x => x.Claimer.UserId == ownerId && x.Waifu.Username == name)
                 .Select(x => x.Waifu.UserId)
                 .FirstOrDefault();

    public static async Task<WaifuInfoStats> GetWaifuInfoAsync(this DbContext ctx, ulong userId)
    {
        await ctx.EnsureUserCreatedAsync(userId);

        await ctx.Set<WaifuInfo>()
                 .ToLinqToDBTable()
                 .InsertOrUpdateAsync(() => new()
                     {
                         AffinityId = null,
                         ClaimerId = null,
                         Price = 1,
                         WaifuId = ctx.Set<DiscordUser>().Where(x => x.UserId == userId).Select(x => x.Id).First()
                     },
                     _ => new(),
                     () => new()
                     {
                         WaifuId = ctx.Set<DiscordUser>().Where(x => x.UserId == userId).Select(x => x.Id).First()
                     });

        var toReturn = ctx.Set<WaifuInfo>()
                          .AsQueryable()
                          .Where(w => w.WaifuId
                                      == ctx.Set<DiscordUser>()
                                            .AsQueryable()
                                            .Where(u => u.UserId == userId)
                                            .Select(u => u.Id)
                                            .FirstOrDefault())
                          .Select(w => new WaifuInfoStats
                          {
                              WaifuId = w.WaifuId,
                              FullName =
                                  ctx.Set<DiscordUser>()
                                     .AsQueryable()
                                     .Where(u => u.UserId == userId)
                                     .Select(u => u.Username)
                                     .FirstOrDefault(),
                              AffinityCount =
                                  ctx.Set<WaifuUpdate>()
                                     .AsQueryable()
                                     .Count(x => x.UserId == w.WaifuId
                                                 && x.UpdateType == WaifuUpdateType.AffinityChanged
                                                 && x.NewId != null),
                              AffinityName =
                                  ctx.Set<DiscordUser>()
                                     .AsQueryable()
                                     .Where(u => u.Id == w.AffinityId)
                                     .Select(u => u.Username)
                                     .FirstOrDefault(),
                              ClaimCount = ctx.Set<WaifuInfo>().AsQueryable().Count(x => x.ClaimerId == w.WaifuId),
                              ClaimerName =
                                  ctx.Set<DiscordUser>()
                                     .AsQueryable()
                                     .Where(u => u.Id == w.ClaimerId)
                                     .Select(u => u.Username)
                                     .FirstOrDefault(),
                              DivorceCount =
                                  ctx.Set<WaifuUpdate>()
                                     .AsQueryable()
                                     .Count(x => x.OldId == w.WaifuId
                                                 && x.NewId == null
                                                 && x.UpdateType == WaifuUpdateType.Claimed),
                              Price = w.Price,
                          })
                          .FirstOrDefault();

        if (toReturn is null)
            return null;

        return toReturn;
    }
}