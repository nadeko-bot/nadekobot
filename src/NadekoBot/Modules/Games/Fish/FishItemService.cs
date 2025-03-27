using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Modules.Games.Fish.Db;

namespace NadekoBot.Modules.Games;

/// <summary>
/// Service for managing fish items that users can buy, equip, and use.
/// </summary>
public sealed class FishItemService(
    DbService db,
    ICurrencyService cs,
    FishConfigService fcs) : INService
{
    private IReadOnlyList<FishItem> _items
        => fcs.Data.Items;

    /// <summary>
    /// Gets all available fish items.
    /// </summary>
    public IReadOnlyList<FishItem> GetItems()
        => _items;

    /// <summary>
    /// Gets a specific fish item by ID.
    /// </summary>
    public FishItem? GetItem(int id)
        => _items.FirstOrDefault(i => i.Id == id);

    /// <summary>
    /// Gets all items of a specific type.
    /// </summary>
    public List<FishItem> GetItemsByType(FishItemType type)
        => _items.Where(i => i.ItemType == type).ToList();

    /// <summary>
    /// Gets all items owned by a user.
    /// </summary>
    public async Task<List<(UserFishItem UserItem, FishItem? Item)>> GetUserItemsAsync(ulong userId)
    {
        await using var ctx = db.GetDbContext();

        var userItems = await ctx.GetTable<UserFishItem>()
            .Where(x => x.UserId == userId)
            .ToListAsyncLinqToDB();

        return userItems
            .Select(ui => (ui, GetItem(ui.ItemId)))
            .Where(x => x.Item2 != null)
            .ToList();
    }

    /// <summary>
    /// Gets all equipped items for a user.
    /// </summary>
    public async Task<List<(UserFishItem UserItem, FishItem Item)>> GetEquippedItemsAsync(ulong userId)
    {
        await CheckExpiredItemsAsync(userId);

        await using var ctx = db.GetDbContext();
        var items = await ctx.GetTable<UserFishItem>()
            .Where(x => x.UserId == userId && x.IsEquipped)
            .ToListAsyncLinqToDB();

        var output = new List<(UserFishItem, FishItem)>();

        foreach (var item in items)
        {
            var fishItem = GetItem(item.ItemId);
            if (fishItem is not null)
                output.Add((item, fishItem));
        }

        return output;
    }

    /// <summary>
    /// Buys an item for a user.
    /// </summary>
    public async Task<OneOf.OneOf<FishItem, BuyResult>> BuyItemAsync(ulong userId, int itemId)
    {
        var item = GetItem(itemId);
        if (item is null)
            return BuyResult.NotFound;

        await using var ctx = db.GetDbContext();

        var removed = await cs.RemoveAsync(userId, item.Price, new("fish_item_purchase", item.Name));
        if (!removed)
            return BuyResult.InsufficientFunds;

        // Add item to user's inventory
        await ctx.GetTable<UserFishItem>()
            .InsertAsync(() => new UserFishItem
            {
                UserId = userId,
                ItemId = itemId,
                ItemType = item.ItemType,
                UsesLeft = item.Uses,
                IsEquipped = false,
            });

        return item;
    }

    /// <summary>
    /// Equips an item for a user.
    /// </summary>
    public async Task<FishItem?> EquipItemAsync(ulong userId, int index)
    {
        await using var ctx = db.GetDbContext();
        await using var tr = await ctx.Database.BeginTransactionAsync();
        try
        {
            var userItem = await ctx.GetTable<UserFishItem>()
                .Where(x => x.UserId == userId)
                .Skip(index - 1)
                .Take(1)
                .FirstOrDefaultAsync();

            if (userItem is null)
                return null;

            var fishItem = GetItem(userItem.ItemId);

            if (fishItem is null)
                return null;

            if (userItem.ItemType == FishItemType.Potion)
            {
                var query = ctx.GetTable<UserFishItem>()
                    .Where(x => x.Id == userItem.Id && !x.IsEquipped)
                    .Set(x => x.IsEquipped, true);

                if (fishItem.DurationMinutes is { } dur)
                    query = query
                        .Set(x => x.ExpiresAt, DateTime.UtcNow.AddMinutes(dur));

                await query.UpdateAsync();
                await tr.CommitAsync();
                return fishItem;
            }

            // UnEquip any currently equipped item of the same type
            // and equip current one
            await ctx.GetTable<UserFishItem>()
                .Where(x => x.UserId == userId && x.ItemType == userItem.ItemType)
                .Set(x => x.IsEquipped, x => x.Id == userItem.Id)
                .UpdateAsync();

            await tr.CommitAsync();

            return fishItem;
        }
        catch
        {
            await tr.RollbackAsync();
            return null;
        }
    }

    /// <summary>
    /// Unequips an item for a user.
    /// </summary>
    public async Task<UnequipResult> UnequipItemAsync(ulong userId, FishItemType itemType)
    {
        // can't unequip potions
        if (itemType == FishItemType.Potion)
            return UnequipResult.Potion;

        await using var ctx = db.GetDbContext();

        var affected = await ctx.GetTable<UserFishItem>()
            .Where(x => x.UserId == userId && x.ItemType == itemType && x.IsEquipped)
            .Set(x => x.IsEquipped, false)
            .UpdateAsync();

        if (affected > 0)
            return UnequipResult.Success;
        else
            return UnequipResult.NotFound;
    }

    /// <summary>
    /// Gets the multipliers from a user's equipped items.
    /// </summary>
    public async Task<FishMultipliers> GetUserMultipliersAsync(ulong userId)
    {
        var equippedItems = await GetEquippedItemsAsync(userId);

        var multipliers = new FishMultipliers();

        foreach (var (_, item) in equippedItems)
        {
            multipliers.FishMultiplier *= item.FishMultiplier ?? 1;
            multipliers.TrashMultiplier *= item.TrashMultiplier ?? 1;
            multipliers.StarMultiplier *= item.MaxStarMultiplier ?? 1;
            multipliers.RareMultiplier *= item.RareMultiplier ?? 1;
            multipliers.FishingSpeedMultiplier *= item.FishingSpeedMultiplier ?? 1;
        }

        return multipliers;
    }

    /// <summary>
    /// Uses a bait item (reduces uses left) when fishing.
    /// </summary>
    public async Task<bool> UseBaitAsync(ulong userId)
    {
        await using var ctx = db.GetDbContext();

        var updated = await ctx.GetTable<UserFishItem>()
            .Where(x =>
                x.UserId == userId &&
                x.ItemType == FishItemType.Bait &&
                x.IsEquipped)
            .Set(x => x.UsesLeft, x => x.UsesLeft - 1)
            .UpdateWithOutputAsync((o, n) => n);

        if (updated.Length == 0)
            return false;

        if (updated[0].UsesLeft <= 0)
        {
            await ctx.GetTable<UserFishItem>()
                .DeleteAsync(x => x.Id == updated[0].Id);
        }

        return true;
    }

    /// <summary>
    /// Checks and removes expired items.
    /// </summary>
    public async Task CheckExpiredItemsAsync(ulong userId)
    {
        await using var ctx = db.GetDbContext();

        var now = DateTime.UtcNow;

        await ctx.GetTable<UserFishItem>()
            .Where(x => x.UserId == userId && x.ExpiresAt.HasValue && x.ExpiresAt < now)
            .DeleteAsync();
    }
}

/// <summary>
/// Represents the result of a buy operation.
/// </summary>
public enum BuyResult
{
    NotFound,
    InsufficientFunds
}

/// <summary>
/// Represents the result of an equip operation.
/// </summary>
public enum UnequipResult
{
    Success,
    NotFound,
    Potion
}

/// <summary>
/// Contains multipliers applied to fishing based on equipped items.
/// </summary>
public class FishMultipliers
{
    public double FishMultiplier { get; set; } = 1.0;
    public double TrashMultiplier { get; set; } = 1.0;
    public double StarMultiplier { get; set; } = 1.0;
    public double RareMultiplier { get; set; } = 1.0;
    public double FishingSpeedMultiplier { get; set; } = 1.0;
}