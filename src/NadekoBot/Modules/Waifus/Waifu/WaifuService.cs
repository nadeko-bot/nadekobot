using LinqToDB;
using LinqToDB.Data;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Waifus.Waifu.Db;
using NadekoBot.Modules.Games.Quests;
using NadekoBot.Modules.Patronage;
using OneOf;
using OneOf.Types;

namespace NadekoBot.Modules.Waifus.Waifu;

#region Result Types

public readonly struct ErrSelfNotAllowed;
public readonly struct ErrNoActionsLeft;
public readonly struct NoEffect;
public readonly struct ErrWaifuNotFound;
public readonly struct ErrAlreadyOptedIn;
public readonly struct ErrNotOptedIn;
public readonly struct ErrInsufficientFunds;
public readonly struct ErrAlreadyBacking;
public readonly struct ErrNotBacking;
public readonly struct ErrHasFans;
public readonly struct ErrNotManager;
public readonly struct ErrInvalidPercent;
public readonly struct ErrOutsideBuyWindow;
public readonly struct ErrPriceTooLow;

[GenerateOneOf]
public sealed partial class OptInResult : OneOfBase<ErrAlreadyOptedIn, ErrInsufficientFunds, Success>;

[GenerateOneOf]
public sealed partial class OptOutResult : OneOfBase<ErrNotOptedIn, ErrHasFans, Success>;

[GenerateOneOf]
public sealed partial class BackResult : OneOfBase<ErrAlreadyBacking, ErrWaifuNotFound, ErrSelfNotAllowed, Success>;

[GenerateOneOf]
public sealed partial class StopBackingResult : OneOfBase<ErrNotBacking, Success>;

[GenerateOneOf]
public sealed partial class ImproveMoodResult : OneOfBase<ErrSelfNotAllowed, ErrNoActionsLeft, ErrWaifuNotFound, NoEffect, Success<int>>;

[GenerateOneOf]
public sealed partial class ImproveFoodResult : OneOfBase<ErrSelfNotAllowed, ErrNoActionsLeft, ErrWaifuNotFound, NoEffect, Success<int>>;

[GenerateOneOf]
public sealed partial class SetFeeResult : OneOfBase<ErrWaifuNotFound, ErrNotOptedIn, ErrInvalidPercent, Success>;

public readonly struct ErrItemNotAvailable;

[GenerateOneOf]
public sealed partial class GiftResult : OneOfBase<ErrSelfNotAllowed, ErrInsufficientFunds, ErrWaifuNotFound, ErrItemNotAvailable, Success<WaifuGiftItem>>;

[GenerateOneOf]
public sealed partial class BuyManagerResult : OneOfBase<ErrOutsideBuyWindow, ErrInsufficientFunds, ErrWaifuNotFound, ErrPriceTooLow, ErrSelfNotAllowed, Success<BuyManagerInfo>>;

[GenerateOneOf]
public sealed partial class ResignManagerResult : OneOfBase<ErrNotManager, Success>;

public readonly struct ErrNoPendingPayout;

[GenerateOneOf]
public sealed partial class ClaimPayoutResult : OneOfBase<ErrNoPendingPayout, Success<long>>;

/// <summary>
/// Details about a successful manager purchase.
/// </summary>
public sealed class BuyManagerInfo
{
    public long PricePaid { get; init; }
    public ulong? OldManagerId { get; init; }
    public long OldManagerRefund { get; init; }
    public long WaifuPayout { get; init; }
    public long Burned { get; init; }
}



#endregion

/// <summary>
/// Service for managing the Waifu backing system.
/// </summary>
public sealed class WaifuService(
    DbService db,
    IBotCache cache,
    ICurrencyService cs,
    DiscordSocketClient client,
    WaifuConfigService configService,
    IPatronageService ps,
    QuestService? quests = null,
    TimeProvider? timeProvider = null
) : INService, IReadyExecutor
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    private double CycleHours => configService.Data.CycleHours;
    private double BaseReturnRate => configService.Data.BaseReturnRate;
    private double CyclesPerYear => 365.25 * 24 / CycleHours;

    private const int PATRON_MAX_ACTIONS = 3;

    /// <summary>
    /// Gets the maximum number of mood/food actions for a user per day.
    /// Active patrons get 3, everyone else gets the configured default.
    /// </summary>
    public async Task<int> GetMaxActionsAsync(ulong userId)
    {
        var patron = await ps.GetPatronAsync(userId);
        return patron is { IsActive: true }
            ? PATRON_MAX_ACTIONS
            : configService.Data.MaxDailyActions;
    }

    /// <summary>
    /// Gets all gift items from config as WaifuGiftItem records.
    /// </summary>
    public IReadOnlyList<WaifuGiftItem> GetAllItems()
        => WaifuGiftItems.FromConfig(configService.Data.Items);

    /// <summary>
    /// Epoch: Tuesday, January 7, 2025 00:00 UTC.
    /// </summary>
    private static readonly DateTime Epoch = new(2025, 1, 7, 0, 0, 0, DateTimeKind.Utc);

    #region Initialization

    /// <summary>
    /// Initializes background loops on bot ready.
    /// </summary>
    public Task OnReadyAsync()
    {
        if (client.ShardId != 0)
            return Task.CompletedTask;

        _ = Task.Run(DecayLoopAsync);
        _ = Task.Run(CycleLoopAsync);

        return Task.CompletedTask;
    }

    #endregion

    #region Cycle Calculations

    private static TypedKey<int> GetActionsKey(ulong userId)
        => new($"wnh:actions:{userId}");

    /// <summary>
    /// Gets the current cycle number.
    /// </summary>
    public int GetCurrentCycle()
        => (int)((_timeProvider.GetUtcNow().UtcDateTime - Epoch).TotalHours / CycleHours);

    /// <summary>
    /// Gets when a cycle starts.
    /// </summary>
    public DateTime GetCycleStartTime(int? cycle = null)
    {
        var c = cycle ?? GetCurrentCycle();
        return Epoch.AddHours(c * CycleHours);
    }

    /// <summary>
    /// Gets when the next cycle starts.
    /// </summary>
    public DateTime GetNextCycleTime()
        => GetCycleStartTime(GetCurrentCycle() + 1);

    /// <summary>
    /// Checks if we're within the buy window (first N hours of cycle).
    /// </summary>
    public bool IsWithinBuyWindow()
        => _timeProvider.GetUtcNow().UtcDateTime < GetCycleStartTime().AddHours(configService.Data.BuyWindowHours);

    /// <summary>
    /// Gets the fraction of the current cycle that has elapsed (0.0 to 1.0).
    /// </summary>
    public double GetCycleProgressFraction()
    {
        var start = GetCycleStartTime();
        var elapsed = (_timeProvider.GetUtcNow().UtcDateTime - start).TotalHours;
        return Math.Clamp(elapsed / CycleHours, 0.0, 1.0);
    }

    /// <summary>
    /// Gets the time remaining until the next cycle payout.
    /// </summary>
    public TimeSpan GetTimeUntilPayout()
    {
        var next = GetNextCycleTime();
        var remaining = next - _timeProvider.GetUtcNow().UtcDateTime;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    #endregion

    #region Query Methods

    /// <summary>
    /// Gets waifu info for display, using cycle snapshots for backed amounts.
    /// All aggregates are computed server-side in a single query.
    /// </summary>
    public async Task<WaifuInfoDto?> GetWaifuInfoAsync(ulong userId)
    {
        await using var ctx = db.GetDbContext();
        var currentCycle = GetCurrentCycle();

        var fans = ctx.GetTable<WaifuFan>().Where(f => f.WaifuUserId == userId);
        var wc = ctx.GetTable<WaifuCycle>()
            .Where(c => c.WaifuUserId == userId && c.CycleNumber == currentCycle);

        var result = await ctx.GetTable<WaifuInfo>()
            .Where(x => x.UserId == userId)
            .Select(wi => new WaifuInfoDto
            {
                UserId = wi.UserId,
                Mood = wi.Mood,
                Food = wi.Food,
                WaifuFeePercent = wi.WaifuFeePercent,
                Price = wi.Price,
                TotalProduced = wi.TotalProduced,
                ReturnsCap = wi.ReturnsCap,
                ManagerId = wi.ManagerUserId,
                IsHubby = wi.IsHubby,
                CustomAvatarUrl = wi.CustomAvatarUrl,
                Description = wi.Description,
                Quote = wi.Quote,
                FanCount = fans.Count(),
                SnapshotTotalBacked = wc.Select(c => (long?)c.TotalBacked).FirstOrDefault() ?? 0
            })
            .FirstOrDefaultAsyncLinqToDB();

        return result;
    }

    /// <summary>
    /// Gets which waifu a user is backing.
    /// </summary>
    public async Task<ulong?> GetBackingAsync(ulong userId)
    {
        await using var ctx = db.GetDbContext();
        return await ctx.GetTable<WaifuFan>()
            .Where(x => x.UserId == userId)
            .Select(x => (ulong?)x.WaifuUserId)
            .FirstOrDefaultAsyncLinqToDB();
    }

    /// <summary>
    /// Checks whether the user is a waifu (opted in).
    /// </summary>
    public async Task<bool> IsWaifuAsync(ulong userId)
    {
        await using var ctx = db.GetDbContext();
        return await ctx.GetTable<WaifuInfo>()
            .AnyAsyncLinqToDB(x => x.UserId == userId);
    }

    /// <summary>
    /// Checks whether the user is a fan (backing someone).
    /// </summary>
    public async Task<bool> IsFanAsync(ulong userId)
    {
        await using var ctx = db.GetDbContext();
        return await ctx.GetTable<WaifuFan>()
            .AnyAsyncLinqToDB(x => x.UserId == userId);
    }

    /// <summary>
    /// Checks whether the user is the manager of any waifu.
    /// </summary>
    public async Task<bool> IsManagerAsync(ulong userId)
    {
        await using var ctx = db.GetDbContext();
        return await ctx.GetTable<WaifuInfo>()
            .AnyAsyncLinqToDB(x => x.ManagerUserId == userId);
    }

    /// <summary>
    /// Checks whether the user is the manager of a specific waifu.
    /// </summary>
    public async Task<bool> IsManagingAsync(ulong userId, ulong waifuUserId)
    {
        await using var ctx = db.GetDbContext();
        return await ctx.GetTable<WaifuInfo>()
            .AnyAsyncLinqToDB(x => x.UserId == waifuUserId && x.ManagerUserId == userId);
    }

    /// <summary>
    /// Gets a page of fans for a waifu, ordered by backing date.
    /// </summary>
    public async Task<List<FanInfoDto>> GetFansAsync(ulong waifuUserId, int page = 0, int pageSize = 10)
    {
        await using var ctx = db.GetDbContext();

        return await ctx.GetTable<WaifuFan>()
            .Where(f => f.WaifuUserId == waifuUserId)
            .LeftJoin(ctx.GetTable<DiscordUser>(),
                (f, du) => f.UserId == du.UserId,
                (f, du) => new FanInfoDto
                {
                    UserId = f.UserId,
                    Username = du.Username,
                    BackedAt = f.DelegatedAt
                })
            .OrderBy(x => x.BackedAt)
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToListAsyncLinqToDB();
    }

    /// <summary>
    /// Gets remaining actions for a user today.
    /// </summary>
    public async Task<int> GetRemainingActionsAsync(ulong userId)
    {
        var actionsKey = GetActionsKey(userId);
        var actionsUsed = await cache.GetAsync(actionsKey);
        var max = await GetMaxActionsAsync(userId);
        return max - (actionsUsed.TryPickT0(out var used, out _) ? used : 0);
    }

    /// <summary>
    /// Gets leaderboard of all waifus ordered by the specified criteria.
    /// </summary>
    public async Task<List<LeaderboardEntryDto>> GetLeaderboardAsync(
        WaifuLbOrder order = WaifuLbOrder.Backing,
        int page = 0,
        int pageSize = 9)
    {
        await using var ctx = db.GetDbContext();
        var currentCycle = GetCurrentCycle();

        var query = ctx.GetTable<WaifuInfo>()
            .LeftJoin(
                ctx.GetTable<WaifuCycle>().Where(wc => wc.CycleNumber == currentCycle),
                (wi, wc) => wi.UserId == wc.WaifuUserId,
                (wi, wc) => new { wi, wc })
            .LeftJoin(ctx.GetTable<DiscordUser>(),
                (x, du) => x.wi.UserId == du.UserId,
                (x, du) => new { x.wi, x.wc, du });

        var ordered = order == WaifuLbOrder.Price
            ? query.OrderByDescending(x => x.wi.Price)
            : query.OrderByDescending(x => x.wc != null ? x.wc.TotalBacked : 0);

        var rows = await ordered
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToListAsyncLinqToDB();

        return rows.Select(r => new LeaderboardEntryDto
        {
            UserId = r.wi.UserId,
            Username = r.du?.Username,
            Price = r.wi.Price,
            Mood = r.wi.Mood,
            Food = r.wi.Food,
            ReturnsCap = r.wi.ReturnsCap,
            SnapshotTotalBacked = r.wc?.TotalBacked ?? 0,
            HasManager = r.wi.ManagerUserId is not null
        }).ToList();
    }

    #endregion

    #region Opt In/Out

    /// <summary>
    /// Opts a user into the waifu system.
    /// </summary>
    public async Task<OptInResult> OptInAsync(ulong userId, bool isHubby = false)
    {
        await using var ctx = db.GetDbContext();
        var conf = configService.Data;

        var existing = await ctx.GetTable<WaifuInfo>()
            .AnyAsyncLinqToDB(x => x.UserId == userId);

        if (existing)
            return new ErrAlreadyOptedIn();

        if (!await cs.RemoveAsync(userId, conf.OptInCost, new("waifu", "opt-in")))
            return new ErrInsufficientFunds();

        await ctx.GetTable<WaifuInfo>()
            .InsertAsync(() => new()
            {
                UserId = userId,
                IsHubby = isHubby,
                Mood = 500,
                Food = 500,
                WaifuFeePercent = 5,
                Price = conf.OptInCost / 10,
                ReturnsCap = conf.DefaultReturnsCap,
                LastDecayTime = _timeProvider.GetUtcNow().UtcDateTime,
                TotalProduced = 0
            });

        return new Success();
    }

    /// <summary>
    /// Opts a user out of the waifu system.
    /// </summary>
    public async Task<OptOutResult> OptOutAsync(ulong userId)
    {
        await using var ctx = db.GetDbContext();

        var hasFans = await ctx.GetTable<WaifuFan>()
            .AnyAsyncLinqToDB(x => x.WaifuUserId == userId);

        if (hasFans)
            return new ErrHasFans();

        var deleted = await ctx.GetTable<WaifuInfo>()
            .Where(x => x.UserId == userId)
            .DeleteAsync();

        if (deleted == 0)
            return new ErrNotOptedIn();

        return new Success();
    }

    #endregion

    #region Backing

    /// <summary>
    /// Becomes a fan of a waifu, or switches to a new waifu.
    /// </summary>
    public async Task<BackResult> BecomeFanAsync(ulong userId, ulong waifuUserId)
    {
        if (userId == waifuUserId)
            return new ErrSelfNotAllowed();

        await using var ctx = db.GetDbContext();

        var existingFan = await ctx.GetTable<WaifuFan>()
            .Where(x => x.UserId == userId)
            .FirstOrDefaultAsyncLinqToDB();

        if (existingFan is not null)
        {
            if (existingFan.WaifuUserId == waifuUserId)
                return new ErrAlreadyBacking();

            await ctx.GetTable<WaifuFan>()
                .Where(x => x.UserId == userId)
                .DeleteAsync();
        }

        var waifuExists = await ctx.GetTable<WaifuInfo>()
            .AnyAsyncLinqToDB(x => x.UserId == waifuUserId);

        if (!waifuExists)
            return new ErrWaifuNotFound();

        await ctx.GetTable<WaifuFan>()
            .InsertAsync(() => new()
            {
                UserId = userId,
                WaifuUserId = waifuUserId,
                DelegatedAt = _timeProvider.GetUtcNow().UtcDateTime
            });

        return new Success();
    }

    /// <summary>
    /// Stops being a fan.
    /// </summary>
    public async Task<StopBackingResult> StopBeingFanAsync(ulong userId)
    {
        await using var ctx = db.GetDbContext();

        var deleted = await ctx.GetTable<WaifuFan>()
            .Where(x => x.UserId == userId)
            .DeleteAsync();

        if (deleted == 0)
            return new ErrNotBacking();

        return new Success();
    }

    #endregion

    #region Manager Resign

    /// <summary>
    /// Resigns from managing a specific waifu. No money changes hands; price drops to MinPrice.
    /// </summary>
    public async Task<ResignManagerResult> ResignManagerAsync(ulong userId, ulong waifuUserId)
    {
        await using var ctx = db.GetDbContext();
        var conf = configService.Data;

        var updated = await ctx.GetTable<WaifuInfo>()
            .Where(x => x.UserId == waifuUserId && x.ManagerUserId == userId)
            .Set(x => x.ManagerUserId, (ulong?)null)
            .Set(x => x.Price, conf.MinPrice)
            .UpdateAsync();

        if (updated == 0)
            return new ErrNotManager();

        return new Success();
    }

    /// <summary>
    /// Gets all waifus managed by the specified user, ordered by price descending.
    /// </summary>
    public async Task<List<ManagedWaifuDto>> GetManagedWaifusAsync(ulong userId)
    {
        await using var ctx = db.GetDbContext();
        var currentCycle = GetCurrentCycle();

        var query = ctx.GetTable<WaifuInfo>()
            .Where(x => x.ManagerUserId == userId)
            .LeftJoin(
                ctx.GetTable<WaifuCycle>().Where(wc => wc.CycleNumber == currentCycle),
                (wi, wc) => wi.UserId == wc.WaifuUserId,
                (wi, wc) => new { wi, wc })
            .LeftJoin(ctx.GetTable<DiscordUser>(),
                (x, du) => x.wi.UserId == du.UserId,
                (x, du) => new { x.wi, x.wc, du });

        var rows = await query.ToListAsyncLinqToDB();

        return rows.Select(r => new ManagedWaifuDto
        {
            WaifuUserId = r.wi.UserId,
            Username = r.du?.Username,
            Price = r.wi.Price,
            TotalBacked = r.wc?.TotalBacked ?? 0
        }).OrderByDescending(x => x.Price).ToList();
    }

    #endregion

    #region Manager Purchase

    /// <summary>
    /// Buys the manager position for a waifu. Buyer specifies bid amount which must be >= 115% of current price.
    /// </summary>
    public async Task<BuyManagerResult> BuyManagerAsync(ulong buyerUserId, ulong waifuUserId, long bidAmount)
    {
        if (buyerUserId == waifuUserId)
            return new ErrSelfNotAllowed();

        await using var ctx = db.GetDbContext();
        var conf = configService.Data;

        if (!IsWithinBuyWindow())
            return new ErrOutsideBuyWindow();

        var wi = await ctx.GetTable<WaifuInfo>()
            .Where(x => x.UserId == waifuUserId)
            .FirstOrDefaultAsyncLinqToDB();

        if (wi is null)
            return new ErrWaifuNotFound();

        var requiredPrice = (long)Math.Ceiling(wi.Price * (1 + conf.ManagerBuyPremium));

        if (bidAmount < requiredPrice)
            return new ErrPriceTooLow();

        if (!await cs.RemoveAsync(buyerUserId, bidAmount, new("waifu", "manager-buy")))
            return new ErrInsufficientFunds();

        var oldManagerId = wi.ManagerUserId;
        var oldPrice = wi.Price;
        var oldManagerRefund = oldManagerId.HasValue ? Math.Min(oldPrice, bidAmount) : 0L;
        var surplus = bidAmount - oldManagerRefund;
        var waifuPayout = (long)(surplus * conf.SurplusWaifuShare);
        var burned = surplus - waifuPayout;

        // Atomic update: only succeed if manager hasn't changed since we read
        int updated;
        if (oldManagerId.HasValue)
        {
            updated = await ctx.GetTable<WaifuInfo>()
                .Where(x => x.Id == wi.Id && x.ManagerUserId == oldManagerId.Value)
                .Set(x => x.ManagerUserId, buyerUserId)
                .Set(x => x.Price, bidAmount)
                .UpdateAsync();
        }
        else
        {
            updated = await ctx.GetTable<WaifuInfo>()
                .Where(x => x.Id == wi.Id && x.ManagerUserId == null)
                .Set(x => x.ManagerUserId, buyerUserId)
                .Set(x => x.Price, bidAmount)
                .UpdateAsync();
        }

        if (updated == 0)
        {
            // Another buyer won the race - refund
            await cs.AddAsync(buyerUserId, bidAmount, new("waifu", "manager-buy-refund"));
            return new ErrPriceTooLow();
        }

        // Refund old manager (if exists)
        if (oldManagerId.HasValue && oldManagerRefund > 0)
            await cs.AddAsync(oldManagerId.Value, oldManagerRefund, new("waifu", "manager-buyout"));

        // Pay waifu
        if (waifuPayout > 0)
            await cs.AddAsync(waifuUserId, waifuPayout, new("waifu", "manager-buy-fee"));

        return new Success<BuyManagerInfo>(new BuyManagerInfo
        {
            PricePaid = bidAmount,
            OldManagerId = oldManagerId,
            OldManagerRefund = oldManagerRefund,
            WaifuPayout = waifuPayout,
            Burned = burned
        });
    }

    #endregion

    #region Waifu Fee

    /// <summary>
    /// Sets the waifu fee (waifu sets their own fee).
    /// </summary>
    public async Task<SetFeeResult> SetWaifuFeeAsync(ulong waifuUserId, int percent)
    {
        if (percent < 1 || percent > 5)
            return new ErrInvalidPercent();

        await using var ctx = db.GetDbContext();

        var updated = await ctx.GetTable<WaifuInfo>()
            .Where(x => x.UserId == waifuUserId)
            .Set(x => x.WaifuFeePercent, percent)
            .UpdateAsync();

        if (updated == 0)
            return new ErrNotOptedIn();

        return new Success();
    }

    #endregion

    #region Mood Actions

    /// <summary>
    /// Improves the mood of a waifu through an action.
    /// </summary>
    public async Task<ImproveMoodResult> ImproveMoodAsync(ulong userId, ulong waifuId, WaifuAction action)
    {
        if (userId == waifuId)
            return new ErrSelfNotAllowed();

        var conf = configService.Data;
        var actionsKey = GetActionsKey(userId);
        var actionsUsed = await cache.GetAsync(actionsKey);
        var used = actionsUsed.TryPickT0(out var u, out _) ? u : 0;
        var maxActions = await GetMaxActionsAsync(userId);

        if (used >= maxActions)
            return new ErrNoActionsLeft();

        var baseMood = conf.BaseMoodIncrease;
        var moodIncrease = action switch
        {
            WaifuAction.Hug => baseMood,
            WaifuAction.Kiss => (int)(baseMood * 1.5),
            WaifuAction.Pat => (int)(baseMood * 0.75),
            _ => baseMood
        };

        await using var ctx = db.GetDbContext();

        var updated = await ctx.GetTable<WaifuInfo>()
            .Where(x => x.UserId == waifuId)
            .Set(x => x.Mood, x => x.Mood + moodIncrease > 1000 ? 1000 : x.Mood + moodIncrease)
            .UpdateWithOutputAsync((o, n) => n.Mood);

        await cache.AddAsync(actionsKey, used + 1, TimeSpan.FromHours(24));

        if (updated.Length == 0)
            return new NoEffect();

        return new Success<int>(moodIncrease);
    }

    /// <summary>
    /// Improves the food of a waifu through a nom action.
    /// </summary>
    public async Task<ImproveFoodResult> ImproveFoodAsync(ulong userId, ulong waifuId)
    {
        if (userId == waifuId)
            return new ErrSelfNotAllowed();

        var conf = configService.Data;
        var actionsKey = GetActionsKey(userId);
        var actionsUsed = await cache.GetAsync(actionsKey);
        var used = actionsUsed.TryPickT0(out var u, out _) ? u : 0;
        var maxActions = await GetMaxActionsAsync(userId);

        if (used >= maxActions)
            return new ErrNoActionsLeft();

        var foodIncrease = conf.BaseFoodIncrease;

        await using var ctx = db.GetDbContext();

        var updated = await ctx.GetTable<WaifuInfo>()
            .Where(x => x.UserId == waifuId)
            .Set(x => x.Food, x => x.Food + foodIncrease > 1000 ? 1000 : x.Food + foodIncrease)
            .UpdateWithOutputAsync((o, n) => n.Food);

        await cache.AddAsync(actionsKey, used + 1, TimeSpan.FromHours(24));

        if (updated.Length == 0)
            return new NoEffect();

        return new Success<int>(foodIncrease);
    }

    #endregion

    #region Gift Actions

    /// <summary>
    /// Gives a gift to a waifu, increasing their stats.
    /// </summary>
    public async Task<GiftResult> GiftAsync(ulong fromUserId, ulong waifuUserId, string itemName, int count = 1)
    {
        if (fromUserId == waifuUserId)
            return new ErrSelfNotAllowed();

        count = Math.Clamp(count, 1, configService.Data.MaxGiftCount);

        var item = WaifuGiftItems.FindTodaysItem(GetAllItems(), itemName);
        if (item is null)
            return new ErrItemNotAvailable();

        var totalCost = item.Price * count;

        if (!await cs.RemoveAsync(fromUserId, totalCost, new("waifu", "gift")))
            return new ErrInsufficientFunds();

        await using var ctx = db.GetDbContext();

        var totalEffect = item.Effect * count;
        int updated;

        if (item.Type == GiftItemType.Food)
        {
            updated = await ctx.GetTable<WaifuInfo>()
                .Where(x => x.UserId == waifuUserId)
                .Set(x => x.Food, x => x.Food + totalEffect > 1000 ? 1000 : x.Food + totalEffect)
                .UpdateAsync();
        }
        else
        {
            updated = await ctx.GetTable<WaifuInfo>()
                .Where(x => x.UserId == waifuUserId)
                .Set(x => x.Mood, x => x.Mood + totalEffect > 1000 ? 1000 : x.Mood + totalEffect)
                .UpdateAsync();
        }

        if (updated == 0)
        {
            await cs.AddAsync(fromUserId, totalCost, new("waifu", "gift-refund"));
            return new ErrWaifuNotFound();
        }

        // Persist gift count
        await ctx.GetTable<WaifuGiftCount>()
            .InsertOrUpdateAsync(() => new()
                {
                    WaifuUserId = waifuUserId,
                    GiftItemId = item.Id,
                    Count = count
                },
                old => new()
                {
                    Count = old.Count + count
                },
                () => new()
                {
                    WaifuUserId = waifuUserId,
                    GiftItemId = item.Id
                });

        if (quests is not null)
            await quests.ReportActionAsync(fromUserId, QuestEventType.WaifuGiftSent);

        return new Success<WaifuGiftItem>(item);
    }

    /// <summary>
    /// Gets all gift counts for a waifu, resolved to gift items.
    /// </summary>
    public async Task<List<(WaifuGiftItem Item, int Count)>> GetGiftCountsAsync(ulong waifuUserId)
    {
        await using var ctx = db.GetDbContext();

        var rows = await ctx.GetTable<WaifuGiftCount>()
            .Where(x => x.WaifuUserId == waifuUserId && x.Count > 0)
            .ToListAsyncLinqToDB();

        var result = new List<(WaifuGiftItem, int)>();

        foreach (var row in rows)
        {
            var item = WaifuGiftItems.FindItemById(GetAllItems(), row.GiftItemId);
            if (item is not null)
                result.Add((item, row.Count));
        }

        return result.OrderByDescending(x => x.Item1.Price).ToList();
    }

    #endregion

    #region Pending Payouts

    /// <summary>
    /// Adds an amount to a user's pending payout (upsert).
    /// </summary>
    internal async Task AddPendingPayoutInternalAsync(NadekoContext ctx, ulong userId, decimal amount)
    {
        await ctx.GetTable<WaifuPendingPayout>()
            .InsertOrUpdateAsync(
                () => new () { UserId = userId, Amount = amount },
                old => new() { Amount = old.Amount + amount },
                () => new () { UserId = userId });
    }

    /// <summary>
    /// Gets the pending payout amount for a user (floored to whole units).
    /// </summary>
    public async Task<long> GetPendingPayoutAsync(ulong userId)
    {
        await using var ctx = db.GetDbContext();
        var amount = await ctx.GetTable<WaifuPendingPayout>()
            .Where(x => x.UserId == userId)
            .Select(x => x.Amount)
            .FirstOrDefaultAsyncLinqToDB();

        return (long)Math.Floor(amount);
    }

    /// <summary>
    /// Claims the pending payout. Floors to whole units, pays to wallet, discards fractional remainder.
    /// </summary>
    public async Task<ClaimPayoutResult> ClaimPayoutAsync(ulong userId)
    {
        await using var ctx = db.GetDbContext();

        var amounts = await ctx.GetTable<WaifuPendingPayout>()
            .Where(x => x.UserId == userId && x.Amount >= 1)
            .DeleteWithOutputAsync(x => x.Amount);

        if (amounts.Length == 0)
            return new ErrNoPendingPayout();

        var claimable = (long)Math.Floor(amounts[0]);
        await cs.AddAsync(userId, claimable, new("waifu", "payout-claim"));

        return new Success<long>(claimable);
    }

    #endregion

    #region Background Jobs

    private async Task DecayLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(2));
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                await DecayInternalAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error decaying waifu stats");
            }
        }
    }

    /// <summary>
    /// Decrements Mood and Food by 1 for all waifus whose LastDecayTime is more than 2 hours ago.
    /// </summary>
    internal async Task DecayInternalAsync()
    {
        await using var ctx = db.GetDbContext();
        var cutoff = _timeProvider.GetUtcNow().UtcDateTime.AddHours(-2);

        await ctx.GetTable<WaifuInfo>()
            .Where(x => x.LastDecayTime < cutoff)
            .Set(x => x.Mood, x => x.Mood > 0 ? x.Mood - 1 : 0)
            .Set(x => x.Food, x => x.Food > 0 ? x.Food - 1 : 0)
            .Set(x => x.LastDecayTime, _timeProvider.GetUtcNow().UtcDateTime)
            .UpdateAsync();
    }

    private async Task CycleLoopAsync()
    {
        var currentCycle = 0;
        while (true)
        {
            try
            {
                currentCycle = GetCurrentCycle();
                var cycleEnd = GetCycleStartTime(currentCycle + 1);

                Log.Information("Waifu cycle #{Cycle}. Payout at {CycleEnd}", currentCycle, cycleEnd);

                await SnapshotCycleInternalAsync(currentCycle);

                var prevCycle = currentCycle - 1;
                if (prevCycle >= 0)
                    await ProcessCycleInternalAsync(prevCycle);

                await CleanupOldCycleDataInternalAsync(currentCycle);

                var delay = cycleEnd - _timeProvider.GetUtcNow().UtcDateTime;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in waifu cycle loop (cycle {Cycle})", currentCycle);
                await Task.Delay(TimeSpan.FromSeconds(30));
            }
        }
    }

    /// <summary>
    /// Snapshots all waifus and their fan balances for a cycle.
    /// Creates WaifuCycle records (waifu-level snapshot) and WaifuCycleSnapshot records (fan balances).
    /// Skips if already snapshotted.
    /// </summary>
    internal async Task SnapshotCycleInternalAsync(int cycleNumber)
    {
        await using var ctx = db.GetDbContext();

        var alreadyExists = await ctx.GetTable<WaifuCycle>()
            .AnyAsyncLinqToDB(x => x.CycleNumber == cycleNumber);

        if (alreadyExists)
            return;

        var conf = configService.Data;
        var managerCutPercent = conf.ManagerCutPercent;

        await using var lctx = ctx.CreateLinqToDBConnection();

        await lctx.ExecuteAsync($"""
            INSERT INTO WaifuCycle
                (WaifuUserId, CycleNumber, ManagerUserId, WaifuFeePercent,
                 ReturnsCap, ManagerCutPercent, Price, Processed, TotalBacked)
            SELECT
                w.UserId, {cycleNumber}, w.ManagerUserId, w.WaifuFeePercent,
                w.ReturnsCap, {managerCutPercent}, w.Price, 0, fb.TotalBacked
            FROM WaifuInfo w
            INNER JOIN (
                SELECT f.WaifuUserId, SUM(COALESCE(b.Balance, 0)) AS TotalBacked
                FROM WaifuFan f
                LEFT JOIN BankUsers b ON b.UserId = f.UserId
                GROUP BY f.WaifuUserId
            ) fb ON fb.WaifuUserId = w.UserId
            WHERE w.ManagerUserId IS NOT NULL AND fb.TotalBacked > 0
            ON CONFLICT (WaifuUserId, CycleNumber) DO NOTHING;
            """);

        await lctx.ExecuteAsync($"""
            INSERT INTO WaifuCycleSnapshot
                (CycleNumber, WaifuUserId, UserId, SnapshotBalance)
            SELECT
                {cycleNumber}, f.WaifuUserId, f.UserId, COALESCE(b.Balance, 0)
            FROM WaifuFan f
            LEFT JOIN BankUsers b ON b.UserId = f.UserId
            WHERE f.WaifuUserId IN (
                SELECT WaifuUserId FROM WaifuCycle WHERE CycleNumber = {cycleNumber}
            )
            ON CONFLICT (CycleNumber, WaifuUserId, UserId) DO NOTHING;
            """);
    }

    /// <summary>
    /// Processes a cycle's unprocessed waifu snapshots: computes payouts, distributes earnings,
    /// and marks snapshots as processed. All computation is done server-side in SQL.
    /// </summary>
    internal async Task ProcessCycleInternalAsync(int cycleNumber)
    {
        await using var ctx = db.GetDbContext();

        var unprocessedCount = await ctx.GetTable<WaifuCycle>()
            .CountAsyncLinqToDB(x => x.CycleNumber == cycleNumber && !x.Processed);

        if (unprocessedCount == 0)
            return;

        Log.Information("Processing waifu cycle #{Cycle}, {Count} waifus", cycleNumber, unprocessedCount);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var cycleRate = BaseReturnRate / CyclesPerYear;

        await using var tx = await ctx.Database.BeginTransactionAsync();
        await using var lctx = ctx.CreateLinqToDBConnection();

        // Step 1: All payouts (fan shares + waifu net + manager cut) in one query
        await lctx.ExecuteAsync($"""
            WITH waifuCalc AS (
                SELECT wc.Id, wc.WaifuUserId, wc.ManagerUserId, wc.WaifuFeePercent,
                    wc.ManagerCutPercent, wc.TotalBacked,
                    CASE WHEN wc.TotalBacked > 0
                         THEN MIN(wc.TotalBacked, wc.ReturnsCap)
                              * {cycleRate} * (wi.Mood + wi.Food) / 2000.0
                         ELSE 0.0 END AS Returns
                FROM WaifuCycle wc
                JOIN WaifuInfo wi ON wi.UserId = wc.WaifuUserId
                WHERE wc.CycleNumber = {cycleNumber} AND NOT wc.Processed AND wc.ManagerUserId != 0
            ),
            waifuSplits AS (
                SELECT Id, WaifuUserId, ManagerUserId, TotalBacked, Returns,
                    Returns * WaifuFeePercent / 100.0 * ManagerCutPercent AS ManagerCut,
                    Returns * WaifuFeePercent / 100.0
                        - Returns * WaifuFeePercent / 100.0 * ManagerCutPercent AS WaifuNet,
                    Returns - Returns * WaifuFeePercent / 100.0 AS FanPool
                FROM waifuCalc
                WHERE Returns >= 1.0
            )
            INSERT INTO WaifuPendingPayout (UserId, Amount)
            SELECT UserId, SUM(Amount) FROM (
                SELECT cs.UserId, ws.FanPool * cs.SnapshotBalance * 1.0 / ws.TotalBacked AS Amount
                FROM waifuSplits ws
                JOIN WaifuCycleSnapshot cs
                    ON cs.WaifuUserId = ws.WaifuUserId AND cs.CycleNumber = {cycleNumber}
                WHERE ws.TotalBacked > 0
                UNION ALL
                SELECT ws.WaifuUserId, ws.WaifuNet FROM waifuSplits ws WHERE ws.WaifuNet > 0
                UNION ALL
                SELECT ws.ManagerUserId, ws.ManagerCut FROM waifuSplits ws WHERE ws.ManagerCut > 0
            ) grouped
            GROUP BY UserId
            ON CONFLICT (UserId) DO UPDATE
            SET Amount = WaifuPendingPayout.Amount + EXCLUDED.Amount;
            """);

        // Mark all cycles as processed
        await ctx.GetTable<WaifuCycle>()
            .Where(wc => wc.CycleNumber == cycleNumber && !wc.Processed)
            .Set(wc => wc.Processed, true)
            .Set(wc => wc.ProcessedAt, now)
            .UpdateAsync();

        // Update TotalProduced on WaifuInfo
        await ctx.GetTable<WaifuInfo>()
            .InnerJoin(
                ctx.GetTable<WaifuCycle>().Where(wc => wc.CycleNumber == cycleNumber && wc.TotalBacked > 0),
                (wi, wc) => wi.UserId == wc.WaifuUserId,
                (wi, wc) => new { wi, wc })
            .Set(x => x.wi.TotalProduced,
                x => x.wi.TotalProduced
                     + (long)((x.wc.TotalBacked < x.wc.ReturnsCap ? x.wc.TotalBacked : x.wc.ReturnsCap)
                              * cycleRate * (x.wi.Mood + x.wi.Food) / 2000.0))
            .UpdateAsync();

        await tx.CommitAsync();

        Log.Information("Waifu cycle #{Cycle} complete", cycleNumber);
    }

    /// <summary>
    /// Deletes snapshot and cycle records older than 8 cycles.
    /// </summary>
    internal async Task CleanupOldCycleDataInternalAsync(int currentCycle)
    {
        var cutoff = currentCycle - 8;
        if (cutoff < 0)
            return;

        await using var ctx = db.GetDbContext();

        var deletedSnapshots = await ctx.GetTable<WaifuCycleSnapshot>()
            .Where(x => x.CycleNumber < cutoff)
            .DeleteAsync();

        var deletedCycles = await ctx.GetTable<WaifuCycle>()
            .Where(x => x.CycleNumber < cutoff)
            .DeleteAsync();

        if (deletedSnapshots > 0 || deletedCycles > 0)
        {
            Log.Information("Cleaned up old cycle data: {Snapshots} snapshots, {Cycles} cycle records (before cycle #{Cutoff})",
                deletedSnapshots, deletedCycles, cutoff);
        }
    }

    #endregion
}

#region DTOs

/// <summary>
/// DTO for waifu info display.
/// </summary>
public sealed class WaifuInfoDto
{
    public ulong UserId { get; init; }
    public int Mood { get; init; }
    public int Food { get; init; }
    public int WaifuFeePercent { get; init; }
    public long Price { get; init; }
    public long TotalProduced { get; init; }
    public long ReturnsCap { get; init; }

    /// <summary>
    /// Number of fans currently in WaifuFan table (active).
    /// </summary>
    public int FanCount { get; init; }

    /// <summary>
    /// Total backed amount from the current cycle's snapshot (locked at cycle start).
    /// Falls back to 0 if no snapshot exists yet.
    /// </summary>
    public long SnapshotTotalBacked { get; init; }

    public ulong? ManagerId { get; init; }
    public bool IsHubby { get; init; }
    public string? CustomAvatarUrl { get; init; }
    public string? Description { get; init; }
    public string? Quote { get; init; }
}

/// <summary>
/// DTO for fan info.
/// </summary>
public sealed class FanInfoDto
{
    public ulong UserId { get; init; }

    /// <summary>
    /// Cached username from the DiscordUser table.
    /// </summary>
    public string? Username { get; init; }

    public DateTime BackedAt { get; init; }
}

/// <summary>
/// DTO for leaderboard entry.
/// </summary>
public sealed class LeaderboardEntryDto
{
    public ulong UserId { get; init; }
    public string? Username { get; init; }
    public long Price { get; init; }
    public int Mood { get; init; }
    public int Food { get; init; }
    public long ReturnsCap { get; init; }
    public long SnapshotTotalBacked { get; init; }
    public bool HasManager { get; init; }
}

/// <summary>
/// DTO for a waifu managed by the user.
/// </summary>
public sealed class ManagedWaifuDto
{
    public ulong WaifuUserId { get; init; }
    public string? Username { get; init; }
    public long Price { get; init; }
    public long TotalBacked { get; init; }
}

/// <summary>
/// Sort order for the waifu leaderboard.
/// </summary>
public enum WaifuLbOrder
{
    Backing,
    Price
}

#endregion
