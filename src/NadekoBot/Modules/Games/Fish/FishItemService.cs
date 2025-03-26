using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Modules.Games.Fish.Db;
using NadekoBot.Services.Currency;

namespace NadekoBot.Modules.Games.Fish;

/// <summary>
/// Service for managing fish items that users can buy, equip, and use.
/// </summary>
public sealed class FishItemService(DbService db, ICurrencyService cs, IBotCache cache) : INService
{
    private readonly IReadOnlyList<FishItem> _items;

    /// <summary>
    /// Gets all available fish items.
    /// </summary>
    public List<FishItem> GetItems() => _items;

    /// <summary>
    /// Gets a specific fish item by ID.
    /// </summary>
    public FishItem GetItem(int id) => _items.FirstOrDefault(i => i.Id == id);

    /// <summary>
    /// Gets all items of a specific type.
    /// </summary>
    public List<FishItem> GetItemsByType(FishItemType type) => _items.Where(i => i.ItemType == type).ToList();

    /// <summary>
    /// Gets all items owned by a user.
    /// </summary>
    public async Task<List<(UserFishItem UserItem, FishItem Item)>> GetUserItemsAsync(ulong userId)
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
    public async Task<Dictionary<FishItemType, (UserFishItem UserItem, FishItem Item)>> GetEquippedItemsAsync(ulong userId)
    {
        await using var ctx = db.GetDbContext();
        
        var userItems = await ctx.GetTable<UserFishItem>()
            .Where(x => x.UserId == userId && x.IsEquipped)
            .ToListAsyncLinqToDB();
        
        return userItems
            .Select(ui => (ui, GetItem(ui.ItemId)))
            .Where(x => x.Item2 != null)
            .ToDictionary(x => x.Item2.ItemType);
    }

    /// <summary>
    /// Buys an item for a user.
    /// </summary>
    public async Task<BuyResult> BuyItemAsync(ulong userId, int itemId)
    {
        var item = GetItem(itemId);
        if (item == null)
            return BuyResult.NotFound;
        
        await using var ctx = db.GetDbContext();
        
        // Check if user already owns this item
        var exists = await ctx.GetTable<UserFishItem>()
            .AnyAsyncLinqToDB(x => x.UserId == userId && x.ItemId == itemId);
        
        if (exists)
            return BuyResult.AlreadyOwned;
        
        // Try to remove currency
        var txData = new TxData("fish_item_purchase", item.Name);
        
        var removed = await cs.RemoveAsync(userId, item.Price, txData);
        if (!removed)
            return BuyResult.InsufficientFunds;
        
        // Add item to user's inventory
        var userItem = new UserFishItem
        {
            UserId = userId,
            ItemId = itemId,
            ItemType = item.ItemType,
            UsesLeft = item.Uses,
        };
        
        await ctx.GetTable<UserFishItem>()
            .InsertAsync(() => userItem);
        
        return BuyResult.Success;
    }

    /// <summary>
    /// Equips an item for a user.
    /// </summary>
    public async Task<EquipResult> EquipItemAsync(ulong userId, int itemId)
    {
        var item = GetItem(itemId);
        if (item == null)
            return EquipResult.NotFound;
        
        await using var ctx = db.GetDbContext();
        
        // Check if user owns this item
        var userItem = await ctx.GetTable<UserFishItem>()
            .FirstOrDefaultAsyncLinqToDB(x => x.UserId == userId && x.ItemId == itemId);
        
        if (userItem == null)
            return EquipResult.NotOwned;
        
        // Check if item has expired
        if (userItem.ExpiresAt.HasValue && userItem.ExpiresAt.Value < DateTime.UtcNow)
            return EquipResult.Expired;
        
        // Check if item has uses left
        if (userItem.UsesLeft.HasValue && userItem.UsesLeft.Value <= 0)
            return EquipResult.NoUsesLeft;
        
        // Unequip any currently equipped item of the same type
        await ctx.GetTable<UserFishItem>()
            .Where(x => x.UserId == userId && x.ItemType == item.ItemType && x.IsEquipped)
            .Set(x => x.IsEquipped, false)
            .UpdateAsync();
        
        // Equip the new item
        await ctx.GetTable<UserFishItem>()
            .Where(x => x.Id == userItem.Id)
            .Set(x => x.IsEquipped, true)
            .UpdateAsync();
        
        return EquipResult.Success;
    }

    /// <summary>
    /// Unequips an item for a user.
    /// </summary>
    public async Task<bool> UnequipItemAsync(ulong userId, FishItemType itemType)
    {
        // can't unequip potions
        if(itemType == FishItemType.Potion)
            return false;

        await using var ctx = db.GetDbContext();
        
        var affected = await ctx.GetTable<UserFishItem>()
            .Where(x => x.UserId == userId && x.ItemType == itemType && x.IsEquipped)
            .Set(x => x.IsEquipped, false)
            .UpdateAsync();
        
        return affected > 0;
    }

    /// <summary>
    /// Gets the multipliers from a user's equipped items.
    /// </summary>
    public async Task<FishMultipliers> GetUserMultipliersAsync(ulong userId)
    {
        var equippedItems = await GetEquippedItemsAsync(userId);
        
        var multipliers = new FishMultipliers();
        
        foreach (var (_, (_, item)) in equippedItems)
        {
            multipliers.FishMultiplier *= item.FishMultiplier;
            multipliers.TrashMultiplier *= item.TrashMultiplier;
            multipliers.MaxStarMultiplier *= item.MaxStarMultiplier;
            multipliers.RareMultiplier *= item.RareMultiplier;
            multipliers.FishingSpeedMultiplier *= item.FishingSpeedMultiplier;
        }
        
        return multipliers;
    }

    /// <summary>
    /// Uses a bait item (reduces uses left) when fishing.
    /// </summary>
    public async Task<bool> UseBaitAsync(ulong userId)
    {
        await using var ctx = db.GetDbContext();
        
        await ctx.GetTable<UserFishItem>()
            .Where(x => 
                x.UserId == userId && 
                x.ItemType == FishItemType.Bait && 
                x.IsEquipped && 
                x.UsesLeft > 0)
            .Set(x => x.UsesLeft, x => x.UsesLeft - 1)
            .UpdateAsync();

        return true;
    }

    /// <summary>
    /// Checks and removes expired items.
    /// </summary>
    public async Task CheckExpiredItemsAsync(ulong userId)
    {
        await using var ctx = db.GetDbContext();
        
        var now = DateTime.UtcNow;
        
        // Unequip expired items
        await ctx.GetTable<UserFishItem>()
            .Where(x => x.UserId == userId && x.ExpiresAt.HasValue && x.ExpiresAt < now && x.IsEquipped)
            .Set(x => x.IsEquipped, false)
            .UpdateAsync();
    }
}

/// <summary>
/// Represents the result of a buy operation.
/// </summary>
public enum BuyResult
{
    Success,
    NotFound,
    AlreadyOwned,
    InsufficientFunds
}

/// <summary>
/// Represents the result of an equip operation.
/// </summary>
public enum EquipResult
{
    Success,
    NotFound,
    NotOwned,
    Expired,
    NoUsesLeft
}

/// <summary>
/// Contains multipliers applied to fishing based on equipped items.
/// </summary>
public class FishMultipliers
{
    public double FishMultiplier { get; set; } = 1.0;
    public double TrashMultiplier { get; set; } = 1.0;
    public double MaxStarMultiplier { get; set; } = 1.0;
    public double RareMultiplier { get; set; } = 1.0;
    public double FishingSpeedMultiplier { get; set; } = 1.0;
}