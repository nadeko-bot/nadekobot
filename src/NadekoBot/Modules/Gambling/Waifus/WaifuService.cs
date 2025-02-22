#nullable disable
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Gambling.Common;
using NadekoBot.Modules.Gambling.Common.Waifu;

namespace NadekoBot.Modules.Gambling.Services;

public class WaifuService : INService, IReadyExecutor
{
    private readonly DbService _db;
    private readonly ICurrencyService _cs;
    private readonly IBotCache _cache;
    private readonly GamblingConfigService _gss;
    private readonly IBotCreds _creds;
    private readonly DiscordSocketClient _client;

    public WaifuService(
        DbService db,
        ICurrencyService cs,
        IBotCache cache,
        GamblingConfigService gss,
        IBotCreds creds,
        DiscordSocketClient client)
    {
        _db = db;
        _cs = cs;
        _cache = cache;
        _gss = gss;
        _creds = creds;
        _client = client;
    }

    public async Task<bool> WaifuTransfer(IUser owner, ulong waifuId, IUser newOwner)
    {
        if (owner.Id == newOwner.Id || waifuId == newOwner.Id)
            return false;

        var settings = _gss.Data;

        await using var uow = _db.GetDbContext();
        var waifu = uow.Set<WaifuInfo>().ByWaifuUserId(waifuId);
        var ownerUser = uow.GetOrCreateUser(owner);

        // owner has to be the owner of the waifu
        if (waifu is null || waifu.ClaimerId != ownerUser.Id)
            return false;

        // if waifu likes the person, gotta pay the penalty
        if (waifu.AffinityId == ownerUser.Id)
        {
            if (!await _cs.RemoveAsync(owner.Id, (long)(waifu.Price * 0.6), new("waifu", "affinity-penalty")))
                // unable to pay 60% penalty
                return false;

            waifu.Price = (long)(waifu.Price * 0.7); // half of 60% = 30% price reduction
            if (waifu.Price < settings.Waifu.MinPrice)
                waifu.Price = settings.Waifu.MinPrice;
        }
        else // if not, pay 10% fee
        {
            if (!await _cs.RemoveAsync(owner.Id, waifu.Price / 10, new("waifu", "transfer")))
                return false;

            waifu.Price = (long)(waifu.Price * 0.95); // half of 10% = 5% price reduction
            if (waifu.Price < settings.Waifu.MinPrice)
                waifu.Price = settings.Waifu.MinPrice;
        }

        //new claimerId is the id of the new owner
        var newOwnerUser = uow.GetOrCreateUser(newOwner);
        waifu.ClaimerId = newOwnerUser.Id;

        await uow.SaveChangesAsync();

        return true;
    }

    public long GetResetPrice(IUser user)
    {
        var settings = _gss.Data;
        using var uow = _db.GetDbContext();
        var waifu = uow.Set<WaifuInfo>().ByWaifuUserId(user.Id);

        if (waifu is null)
            return settings.Waifu.MinPrice;

        var divorces = uow.Set<WaifuUpdate>()
                          .Count(x
                              => x.Old != null
                                 && x.Old.UserId == user.Id
                                 && x.UpdateType == WaifuUpdateType.Claimed
                                 && x.New == null);
        var affs = uow.Set<WaifuUpdate>()
                      .AsQueryable()
                      .Where(w => w.User.UserId == user.Id
                                  && w.UpdateType == WaifuUpdateType.AffinityChanged
                                  && w.New != null)
                      .ToList()
                      .GroupBy(x => x.New)
                      .Count();

        return (long)Math.Ceiling(waifu.Price * 1.25f)
               + ((divorces + affs + 2) * settings.Waifu.Multipliers.WaifuReset);
    }

    public async Task<bool> TryReset(IUser user)
    {
        await using var uow = _db.GetDbContext();
        var price = GetResetPrice(user);
        if (!await _cs.RemoveAsync(user.Id, price, new("waifu", "reset")))
            return false;

        var affs = uow.Set<WaifuUpdate>()
                      .AsQueryable()
                      .Where(w => w.User.UserId == user.Id
                                  && w.UpdateType == WaifuUpdateType.AffinityChanged
                                  && w.New != null);

        var divorces = uow.Set<WaifuUpdate>()
                          .AsQueryable()
                          .Where(x => x.Old != null
                                      && x.Old.UserId == user.Id
                                      && x.UpdateType == WaifuUpdateType.Claimed
                                      && x.New == null);

        //reset changes of heart to 0
        uow.Set<WaifuUpdate>().RemoveRange(affs);
        //reset divorces to 0
        uow.Set<WaifuUpdate>().RemoveRange(divorces);
        var waifu = uow.Set<WaifuInfo>().ByWaifuUserId(user.Id);
        //reset price, remove items
        //remove owner, remove affinity
        waifu.Price = 50;
        waifu.Items.Clear();
        waifu.ClaimerId = null;
        waifu.AffinityId = null;

        //wives stay though

        await uow.SaveChangesAsync();

        return true;
    }

    public async Task<(WaifuInfo, bool, WaifuClaimResult)> ClaimWaifuAsync(IUser user, IUser target, long amount)
    {
        var settings = _gss.Data;
        WaifuClaimResult result;
        WaifuInfo w;
        bool isAffinity;
        await using (var uow = _db.GetDbContext())
        {
            w = uow.Set<WaifuInfo>().ByWaifuUserId(target.Id);
            isAffinity = w?.Affinity?.UserId == user.Id;
            if (w is null)
            {
                var claimer = uow.GetOrCreateUser(user);
                var waifu = uow.GetOrCreateUser(target);
                if (!await _cs.RemoveAsync(user.Id, amount, new("waifu", "claim")))
                    result = WaifuClaimResult.NotEnoughFunds;
                else
                {
                    uow.Set<WaifuInfo>()
                       .Add(w = new()
                       {
                           Waifu = waifu,
                           Claimer = claimer,
                           Affinity = null,
                           Price = amount
                       });
                    uow.Set<WaifuUpdate>()
                       .Add(new()
                       {
                           User = waifu,
                           Old = null,
                           New = claimer,
                           UpdateType = WaifuUpdateType.Claimed
                       });
                    result = WaifuClaimResult.Success;
                }
            }
            else if (isAffinity && amount > w.Price * settings.Waifu.Multipliers.CrushClaim)
            {
                if (!await _cs.RemoveAsync(user.Id, amount, new("waifu", "claim")))
                    result = WaifuClaimResult.NotEnoughFunds;
                else
                {
                    var oldClaimer = w.Claimer;
                    w.Claimer = uow.GetOrCreateUser(user);
                    w.Price = amount + (amount / 4);
                    result = WaifuClaimResult.Success;

                    uow.Set<WaifuUpdate>()
                       .Add(new()
                       {
                           User = w.Waifu,
                           Old = oldClaimer,
                           New = w.Claimer,
                           UpdateType = WaifuUpdateType.Claimed
                       });
                }
            }
            else if (amount >= w.Price * settings.Waifu.Multipliers.NormalClaim) // if no affinity
            {
                if (!await _cs.RemoveAsync(user.Id, amount, new("waifu", "claim")))
                    result = WaifuClaimResult.NotEnoughFunds;
                else
                {
                    var oldClaimer = w.Claimer;
                    w.Claimer = uow.GetOrCreateUser(user);
                    w.Price = amount;
                    result = WaifuClaimResult.Success;

                    uow.Set<WaifuUpdate>()
                       .Add(new()
                       {
                           User = w.Waifu,
                           Old = oldClaimer,
                           New = w.Claimer,
                           UpdateType = WaifuUpdateType.Claimed
                       });
                }
            }
            else
                result = WaifuClaimResult.InsufficientAmount;


            await uow.SaveChangesAsync();
        }

        return (w, isAffinity, result);
    }

    public async Task<(DiscordUser, bool, TimeSpan?)> ChangeAffinityAsync(IUser user, IGuildUser target)
    {
        DiscordUser oldAff = null;
        var success = false;
        TimeSpan? remaining = null;
        await using (var uow = _db.GetDbContext())
        {
            var w = uow.Set<WaifuInfo>().ByWaifuUserId(user.Id);
            var newAff = target is null ? null : uow.GetOrCreateUser(target);
            if (w?.Affinity?.UserId == target?.Id)
            {
                return (null, false, null);
            }

            remaining = await _cache.GetRatelimitAsync(GetAffinityKey(user.Id),
                30.Minutes());

            if (remaining is not null)
            {
            }
            else if (w is null)
            {
                var thisUser = uow.GetOrCreateUser(user);
                uow.Set<WaifuInfo>()
                   .Add(new()
                   {
                       Affinity = newAff,
                       Waifu = thisUser,
                       Price = 1,
                       Claimer = null
                   });
                success = true;

                uow.Set<WaifuUpdate>()
                   .Add(new()
                   {
                       User = thisUser,
                       Old = null,
                       New = newAff,
                       UpdateType = WaifuUpdateType.AffinityChanged
                   });
            }
            else
            {
                if (w.Affinity is not null)
                    oldAff = w.Affinity;
                w.Affinity = newAff;
                success = true;

                uow.Set<WaifuUpdate>()
                   .Add(new()
                   {
                       User = w.Waifu,
                       Old = oldAff,
                       New = newAff,
                       UpdateType = WaifuUpdateType.AffinityChanged
                   });
            }

            await uow.SaveChangesAsync();
        }

        return (oldAff, success, remaining);
    }

    public async Task<IReadOnlyList<WaifuLbResult>> GetTopWaifusAtPage(int page, int perPage = 9)
    {
        await using var uow = _db.GetDbContext();
        return await uow.Set<WaifuInfo>().GetTop(perPage, page * perPage);
    }

    public ulong GetWaifuUserId(ulong ownerId, string name)
    {
        using var uow = _db.GetDbContext();
        return uow.Set<WaifuInfo>().GetWaifuUserId(ownerId, name);
    }

    private static TypedKey<long> GetDivorceKey(ulong userId)
        => new($"waifu:divorce_cd:{userId}");

    private static TypedKey<long> GetAffinityKey(ulong userId)
        => new($"waifu:affinity:{userId}");

    public async Task<(WaifuInfo, DivorceResult, long)> DivorceWaifuAsync(IUser user, ulong targetId)
    {
        DivorceResult result;
        long amount = 0;
        WaifuInfo w;
        await using (var uow = _db.GetDbContext())
        {
            w = uow.Set<WaifuInfo>().ByWaifuUserId(targetId);
            if (w?.Claimer is null || w.Claimer.UserId != user.Id)
            {
                result = DivorceResult.NotYourWife;
            }
            else
            {

                amount = w.Price / 2;

                if (w.Affinity?.UserId == user.Id)
                {
                    await _cs.AddAsync(w.Waifu.UserId, amount, new("waifu", "compensation"));
                    w.Price = (long)Math.Floor(w.Price * _gss.Data.Waifu.Multipliers.DivorceNewValue);
                    result = DivorceResult.SucessWithPenalty;
                }
                else
                {
                    await _cs.AddAsync(user.Id, amount, new("waifu", "refund"));

                    result = DivorceResult.Success;
                }

                var oldClaimer = w.Claimer;
                w.Claimer = null;

                uow.Set<WaifuUpdate>()
                   .Add(new()
                   {
                       User = w.Waifu,
                       Old = oldClaimer,
                       New = null,
                       UpdateType = WaifuUpdateType.Claimed
                   });
            }

            await uow.SaveChangesAsync();
        }

        return (w, result, amount);
    }

    public async Task<bool> GiftWaifuAsync(
        IUser from,
        IUser giftedWaifu,
        WaifuItemModel itemObj,
        int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1, nameof(count));

        if (!await _cs.RemoveAsync(from, itemObj.Price * count, new("waifu", "item")))
            return false;

        var totalValue = itemObj.Price * count;

        await using var uow = _db.GetDbContext();
        var w = uow.Set<WaifuInfo>()
                   .ByWaifuUserId(giftedWaifu.Id,
                       set => set
                              .Include(x => x.Items)
                              .Include(x => x.Claimer));
        if (w is null)
        {
            uow.Set<WaifuInfo>()
               .Add(w = new()
               {
                   Affinity = null,
                   Claimer = null,
                   Price = 1,
                   Waifu = uow.GetOrCreateUser(giftedWaifu)
               });
        }

        if (!itemObj.Negative)
        {
            w.Items.AddRange(Enumerable.Range(0, count)
                                       .Select((_) => new WaifuItem()
                                       {
                                           Name = itemObj.Name.ToLowerInvariant(),
                                           ItemEmoji = itemObj.ItemEmoji
                                       }));

            if (w.Claimer?.UserId == from.Id)
                w.Price += (long)(totalValue * _gss.Data.Waifu.Multipliers.GiftEffect);
            else
                w.Price += totalValue / 2;
        }
        else
        {
            w.Price -= (long)(totalValue * _gss.Data.Waifu.Multipliers.NegativeGiftEffect);
            if (w.Price < 1)
                w.Price = 1;
        }

        await uow.SaveChangesAsync();

        return true;
    }

    public async Task<WaifuInfoStats> GetFullWaifuInfoAsync(ulong targetId)
    {
        await using var uow = _db.GetDbContext();
        var wi = await uow.GetWaifuInfoAsync(targetId);
        if (wi is null)
        {
            wi = new()
            {
                AffinityCount = 0,
                AffinityName = null,
                ClaimCount = 0,
                ClaimerName = null,
                DivorceCount = 0,
                FullName = null,
                Price = 1
            };
        }

        return wi;
    }

    public string GetClaimTitle(int count)
    {
        ClaimTitle title;
        if (count == 0)
            title = ClaimTitle.Lonely;
        else if (count == 1)
            title = ClaimTitle.Devoted;
        else if (count < 3)
            title = ClaimTitle.Rookie;
        else if (count < 6)
            title = ClaimTitle.Schemer;
        else if (count < 10)
            title = ClaimTitle.Dilettante;
        else if (count < 17)
            title = ClaimTitle.Intermediate;
        else if (count < 25)
            title = ClaimTitle.Seducer;
        else if (count < 35)
            title = ClaimTitle.Expert;
        else if (count < 50)
            title = ClaimTitle.Veteran;
        else if (count < 75)
            title = ClaimTitle.Incubis;
        else if (count < 100)
            title = ClaimTitle.Harem_King;
        else
            title = ClaimTitle.Harem_God;

        return title.ToString().Replace('_', ' ');
    }

    public string GetAffinityTitle(int count)
    {
        AffinityTitle title;
        if (count < 1)
            title = AffinityTitle.Pure;
        else if (count < 2)
            title = AffinityTitle.Faithful;
        else if (count < 4)
            title = AffinityTitle.Playful;
        else if (count < 8)
            title = AffinityTitle.Cheater;
        else if (count < 11)
            title = AffinityTitle.Tainted;
        else if (count < 15)
            title = AffinityTitle.Corrupted;
        else if (count < 20)
            title = AffinityTitle.Lewd;
        else if (count < 25)
            title = AffinityTitle.Sloot;
        else if (count < 35)
            title = AffinityTitle.Depraved;
        else
            title = AffinityTitle.Harlot;

        return title.ToString().Replace('_', ' ');
    }

    public IReadOnlyList<WaifuItemModel> GetWaifuItems()
    {
        var conf = _gss.Data;
        return conf.Waifu.Items.Select(x
                       => new WaifuItemModel(x.ItemEmoji,
                           (long)(x.Price * conf.Waifu.Multipliers.AllGiftPrices),
                           x.Name,
                           x.Negative))
                   .ToList();
    }

    private static readonly TypedKey<long> _waifuDecayKey = $"waifu:last_decay";

    public async Task OnReadyAsync()
    {
        // only decay waifu values from shard 0
        if (_client.ShardId != 0)
            return;

        while (true)
        {
            try
            {
                var decay = _gss.Data.Waifu.Decay;

                var unclaimedMulti = 1 - (decay.UnclaimedDecayPercent / 100f);
                var claimedMulti = 1 - (decay.ClaimedDecayPercent / 100f);

                var minPrice = decay.MinPrice;
                var decayInterval = decay.HourInterval;

                if (decayInterval <= 0)
                    continue;

                if ((unclaimedMulti < 0 || unclaimedMulti > 1) && (claimedMulti < 0 || claimedMulti > 1))
                    continue;

                var now = DateTime.UtcNow;
                var nowB = now.ToBinary();

                var result = await _cache.GetAsync(_waifuDecayKey);

                if (result.TryGetValue(out var val))
                {
                    var lastDecay = DateTime.FromBinary(val);
                    var toWait = decayInterval.Hours() - (DateTime.UtcNow - lastDecay);

                    if (toWait > 0.Hours())
                        continue;
                }

                await _cache.AddAsync(_waifuDecayKey, nowB);

                if (unclaimedMulti is > 0 and <= 1)
                {
                    await using var uow = _db.GetDbContext();

                    await uow.GetTable<WaifuInfo>()
                             .Where(x => x.Price > minPrice && x.ClaimerId == null)
                             .UpdateAsync(old => new()
                             {
                                 Price = (long)(old.Price * unclaimedMulti)
                             });
                }

                if (claimedMulti is > 0 and <= 1)
                {
                    await using var uow = _db.GetDbContext();
                    await uow.GetTable<WaifuInfo>()
                             .Where(x => x.Price > minPrice && x.ClaimerId != null)
                             .UpdateAsync(old => new()
                             {
                                 Price = (long)(old.Price * claimedMulti)
                             });
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unexpected error occured in waifu decay loop: {ErrorMessage}", ex.Message);
            }
            finally
            {
                await Task.Delay(1.Hours());
            }
        }
    }

    public async Task<IReadOnlyCollection<string>> GetClaimNames(int waifuId)
    {
        await using var ctx = _db.GetDbContext();
        return await ctx.GetTable<DiscordUser>()
                        .Where(x => ctx.GetTable<WaifuInfo>()
                                       .Where(wi => wi.ClaimerId == waifuId)
                                       .Select(wi => wi.WaifuId)
                                       .Contains(x.Id))
                        .Select(x => x.Username)
                        .ToListAsyncEF();
    }

    public async Task<IReadOnlyCollection<string>> GetFansNames(int waifuId)
    {
        await using var ctx = _db.GetDbContext();
        return await ctx.GetTable<DiscordUser>()
                        .Where(x => ctx.GetTable<WaifuInfo>()
                                       .Where(wi => wi.AffinityId == waifuId)
                                       .Select(wi => wi.WaifuId)
                                       .Contains(x.Id))
                        .Select(x => x.Username)
                        .ToListAsyncEF();
    }

    public async Task<IReadOnlyCollection<WaifuItem>> GetItems(int waifuId)
    {
        await using var ctx = _db.GetDbContext();
        return await ctx.GetTable<WaifuItem>()
                        .Where(x => x.WaifuInfoId
                                    == ctx.GetTable<WaifuInfo>()
                                          .Where(x => x.WaifuId == waifuId)
                                          .Select(x => x.Id)
                                          .FirstOrDefault())
                        .ToListAsyncEF();
    }

    public async Task<IReadOnlyCollection<WaifuClaimsResult>> GetClaimsAsync(ulong userId, int page)
    {
        await using var ctx = _db.GetDbContext();

        var wid = ctx.GetTable<DiscordUser>()
                     .Where(x => x.UserId == userId)
                     .Select(x => x.Id)
                     .FirstOrDefault();

        if (wid == 0)
            return [];

        return await ctx.GetTable<WaifuInfo>()
                        .Where(x => x.ClaimerId == wid)
                        .LeftJoin(ctx.GetTable<DiscordUser>(),
                            (wi, du) => wi.WaifuId == du.Id,
                            (wi, du) => new WaifuClaimsResult(
                                du.Username,
                                du.UserId,
                                wi.Price
                            ))
                        .OrderByDescending(x => x.Price)
                        .Skip(page * 9)
                        .Take(9)
                        .ToListAsyncLinqToDB();
    }
}

public sealed class WaifuClaimsResult(string username, ulong userId, long price)
{
    public string Username { get; } = username;
    public ulong UserId { get; } = userId;
    public long Price { get; } = price;
}