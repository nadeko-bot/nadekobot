#nullable disable
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Gambling.Services;

public sealed class ShopService(DbService db) : IShopService, INService
{
    // Old data can have gaps in Index, so positions are ranks in this order, not raw Index values.
    public static IOrderedQueryable<ShopEntry> Ordered(IQueryable<ShopEntry> entries, ulong guildId)
        => entries.Where(x => x.GuildId == guildId)
                  .OrderBy(x => x.Index)
                  .ThenBy(x => x.Id);

    private static IQueryable<ShopEntry> AtPositionInternal(DbContext uow, ulong guildId, int index)
    {
        var table = uow.GetTable<ShopEntry>();
        return table.Where(x => x.Id
                                == Ordered(table, guildId)
                                   .Skip(index)
                                   .Select(y => y.Id)
                                   .FirstOrDefault());
    }

    private static Task<List<int>> GetOrderedIdsInternalAsync(DbContext uow, ulong guildId)
        => Ordered(uow.GetTable<ShopEntry>(), guildId)
           .Select(x => x.Id)
           .ToListAsyncLinqToDB();

    private static async Task RewriteOrderInternalAsync(DbContext uow, ulong guildId, List<int> ids)
    {
        var table = uow.GetTable<ShopEntry>();

        // SQLite checks the unique (GuildId, Index) index row by row, so all rows leave the 0..n range first.
        await table.Where(x => x.GuildId == guildId)
                   .Set(x => x.Index, x => -x.Index - 1)
                   .UpdateAsync();

        for (var i = 0; i < ids.Count; i++)
        {
            var id = ids[i];
            var newIndex = i;
            await table.Where(x => x.Id == id)
                       .Set(x => x.Index, newIndex)
                       .UpdateAsync();
        }
    }

    public async Task<bool> ChangeEntryPriceAsync(ulong guildId, int index, int newPrice)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(newPrice);

        await using var uow = db.GetDbContext();

        var changed = await AtPositionInternal(uow, guildId, index)
                            .Set(x => x.Price, newPrice)
                            .UpdateAsync();

        return changed > 0;
    }

    public async Task<bool> ChangeEntryNameAsync(ulong guildId, int index, string newName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentNullException(nameof(newName));

        newName = newName.TrimTo(100);

        await using var uow = db.GetDbContext();

        var changed = await AtPositionInternal(uow, guildId, index)
                            .Set(x => x.Name, newName)
                            .UpdateAsync();

        return changed > 0;
    }

    public async Task<bool> SwapEntriesAsync(ulong guildId, int index1, int index2)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index1);
        ArgumentOutOfRangeException.ThrowIfNegative(index2);

        await using var uow = db.GetDbContext();
        await using var tr = await uow.Database.BeginTransactionAsync();

        var ids = await GetOrderedIdsInternalAsync(uow, guildId);
        if (index1 >= ids.Count || index2 >= ids.Count || index1 == index2)
            return false;

        (ids[index1], ids[index2]) = (ids[index2], ids[index1]);

        await RewriteOrderInternalAsync(uow, guildId, ids);
        await tr.CommitAsync();
        return true;
    }

    public async Task<bool> MoveEntryAsync(ulong guildId, int fromIndex, int toIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fromIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(toIndex);

        await using var uow = db.GetDbContext();
        await using var tr = await uow.Database.BeginTransactionAsync();

        var ids = await GetOrderedIdsInternalAsync(uow, guildId);
        if (fromIndex >= ids.Count || toIndex >= ids.Count || fromIndex == toIndex)
            return false;

        var id = ids[fromIndex];
        ids.RemoveAt(fromIndex);
        ids.Insert(toIndex, id);

        await RewriteOrderInternalAsync(uow, guildId, ids);
        await tr.CommitAsync();
        return true;
    }

    public async Task<ShopEntry> RemoveEntryAsync(ulong guildId, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        await using var uow = db.GetDbContext();
        await using var tr = await uow.Database.BeginTransactionAsync();

        var ids = await GetOrderedIdsInternalAsync(uow, guildId);
        if (index >= ids.Count)
            return null;

        var id = ids[index];
        var removed = await uow.Set<ShopEntry>()
                               .AsNoTracking()
                               .Include(x => x.Items)
                               .FirstOrDefaultAsyncEF(x => x.Id == id);

        if (removed is null)
            return null;

        await uow.GetTable<ShopEntry>()
                 .Where(x => x.Id == id)
                 .DeleteAsync();

        ids.RemoveAt(index);
        await RewriteOrderInternalAsync(uow, guildId, ids);
        await tr.CommitAsync();

        return removed;
    }

    public async Task<bool> SetItemRoleRequirementAsync(ulong guildId, int index, ulong? roleId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        await using var uow = db.GetDbContext();

        var changes = await AtPositionInternal(uow, guildId, index)
                            .Set(x => x.RoleRequirement, roleId)
                            .UpdateAsync();

        return changes > 0;
    }

    public Task<ShopEntry> AddShopCommandAsync(ulong guildId, ulong userId, int price, string command)
        => AddInternalAsync(guildId, userId, price, ShopEntryType.Command, command, 0, null, command);

    public Task<ShopEntry> AddShopRoleAsync(ulong guildId, ulong userId, int price, ulong roleId, string roleName)
        => AddInternalAsync(guildId, userId, price, ShopEntryType.Role, "-", roleId, roleName, null);

    public Task<ShopEntry> AddShopListAsync(ulong guildId, ulong userId, int price, string name)
        => AddInternalAsync(guildId, userId, price, ShopEntryType.List, name.TrimTo(100), 0, null, null);

    private async Task<ShopEntry> AddInternalAsync(
        ulong guildId,
        ulong userId,
        int price,
        ShopEntryType type,
        string name,
        ulong roleId,
        string roleName,
        string command)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(price);

        await using var uow = db.GetDbContext();
        var table = uow.GetTable<ShopEntry>();

        // the next index is computed inside the insert, so concurrent adds can not pick the same index
        return await table.InsertWithOutputAsync(() => new()
        {
            GuildId = guildId,
            Index = (table.Where(x => x.GuildId == guildId).Select(x => (int?)x.Index).Max() ?? -1) + 1,
            AuthorId = userId,
            Price = price,
            Type = type,
            Name = name,
            RoleId = roleId,
            RoleName = roleName,
            Command = command,
            DateAdded = DateTime.UtcNow,
        });
    }
}
