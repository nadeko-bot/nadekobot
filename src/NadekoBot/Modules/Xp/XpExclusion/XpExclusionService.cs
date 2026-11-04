using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using LinqToDB;
using Microsoft.EntityFrameworkCore;

namespace NadekoBot.Modules.Xp;

public sealed class XpExclusionService(DbService db, ShardData shardData) : IReadyExecutor, INService
{
    private ConcurrentHashSet<(ulong GuildId, XpExcludedItemType ItemType, ulong ItemId)> _exclusions = new();

    // Lets the xp check skip the roles of a member in servers which exclude no role.
    private ConcurrentDictionary<ulong, int> _roleExclusionCounts = new();

    public async Task OnReadyAsync()
    {
        await using var uow = db.GetDbContext();
        var items = await uow.GetTable<XpExcludedItem>()
            .Where(Queries.GuildOnShard<XpExcludedItem>(x => x.GuildId, shardData.TotalShards, shardData.ShardId))
            .Select(x => new { x.GuildId, x.ItemType, x.ItemId })
            .ToListAsyncLinqToDB();

        var exclusions = new ConcurrentHashSet<(ulong GuildId, XpExcludedItemType ItemType, ulong ItemId)>();
        var roleCounts = new ConcurrentDictionary<ulong, int>();
        foreach (var item in items)
        {
            if (exclusions.Add((item.GuildId, item.ItemType, item.ItemId)) && item.ItemType == XpExcludedItemType.Role)
                roleCounts.AddOrUpdate(item.GuildId, 1, static (_, c) => c + 1);
        }

        _exclusions = exclusions;
        _roleExclusionCounts = roleCounts;
    }
    
    /// <summary>
    /// Toggles exclusion for the specified item. If the item was excluded, it will be included
    /// and vice versa.
    /// </summary>
    /// <param name="guildId">ID of the guild</param>
    /// <param name="itemType">Type of the item to toggle exclusion for</param>
    /// <param name="itemId">ID of the item to toggle exclusion for</param>
    /// <returns>True if the item is now excluded, false if it's no longer excluded</returns>
    public async Task<bool> ToggleExclusionAsync(ulong guildId, XpExcludedItemType itemType, ulong itemId)
    {
        var key = (guildId, itemType, itemId);
        var isExcluded = false;

        await using var uow = db.GetDbContext();
        if (_exclusions.Contains(key))
        {
            isExcluded = false;
            // item exists, remove it
            await uow.GetTable<XpExcludedItem>()
                .Where(x => x.GuildId == guildId 
                       && x.ItemType == itemType 
                       && x.ItemId == itemId)
                .DeleteAsync();
            
            if (_exclusions.TryRemove(key) && itemType == XpExcludedItemType.Role)
                _roleExclusionCounts.AddOrUpdate(guildId, 0, static (_, c) => Math.Max(0, c - 1));
        }
        else
        {
            isExcluded = true;
            // item doesn't exist, add it
            await uow.GetTable<XpExcludedItem>()
                .InsertOrUpdateAsync(() => new XpExcludedItem
                {
                    GuildId = guildId,
                    ItemType = itemType,
                    ItemId = itemId
                },
                _ => new (),
                () => new XpExcludedItem
                {
                    GuildId = guildId,
                    ItemType = itemType,
                    ItemId = itemId
                });
            
            if (_exclusions.Add(key) && itemType == XpExcludedItemType.Role)
                _roleExclusionCounts.AddOrUpdate(guildId, 1, static (_, c) => c + 1);
        }

        return isExcluded;
    }
    
    /// <summary>
    /// Gets a list of all excluded items for a guild.
    /// </summary>
    /// <param name="guildId">ID of the guild</param>
    /// <returns>List of excluded items in the guild</returns>
    public async Task<IReadOnlyList<XpExcludedItem>> GetExclusionsAsync(ulong guildId)
    {
        await using var uow = db.GetDbContext();
        return await uow.GetTable<XpExcludedItem>()
            .AsNoTracking()
            .Where(x => x.GuildId == guildId)
            .ToListAsyncLinqToDB();
    }
    
    /// <summary>
    /// Checks if the specified item is excluded from XP gain.
    /// </summary>
    /// <param name="guildId">ID of the guild</param>
    /// <param name="itemType">Type of the item</param>
    /// <param name="itemId">ID of the item</param>
    /// <returns>True if the item is excluded, otherwise false</returns>
    public bool IsExcluded(ulong guildId, XpExcludedItemType itemType, ulong itemId)
        => _exclusions.Contains((guildId, itemType, itemId));

    public bool HasRoleExclusions(ulong guildId)
        => _roleExclusionCounts.TryGetValue(guildId, out var count) && count > 0;
}