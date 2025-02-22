#nullable disable
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Gambling.Services;

public class ShopService : IShopService, INService
{
    private readonly DbService _db;

    public ShopService(DbService db)
        => _db = db;

    private async Task<IndexedCollection<ShopEntry>> GetEntriesInternal(DbContext uow, ulong guildId)
    {
        var items = await uow.Set<ShopEntry>()
                             .Where(x => x.GuildId == guildId)
                             .ToListAsyncEF();

        return items.ToIndexed();
    }

    public async Task<bool> ChangeEntryPriceAsync(ulong guildId, int index, int newPrice)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(newPrice);

        await using var uow = _db.GetDbContext();

        var changed = await uow.GetTable<ShopEntry>()
                               .Where(x => x.GuildId == guildId && x.Index == index)
                               .UpdateAsync(x => new ShopEntry()
                               {
                                   Price = newPrice,
                               });

        return changed > 0;
    }

    public async Task<bool> ChangeEntryNameAsync(ulong guildId, int index, string newName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentNullException(nameof(newName));

        newName = newName?.TrimTo(100);

        await using var uow = _db.GetDbContext();

        var changed = await uow.GetTable<ShopEntry>()
                               .Where(x => x.GuildId == guildId && x.Index == index)
                               .UpdateAsync(x => new ShopEntry()
                               {
                                   Name = newName,
                               });
        return changed > 0;
    }

    public async Task<bool> SwapEntriesAsync(ulong guildId, int index1, int index2)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index1);
        ArgumentOutOfRangeException.ThrowIfNegative(index2);

        await using var uow = _db.GetDbContext();
        var entries = await GetEntriesInternal(uow, guildId);

        if (index1 >= entries.Count || index2 >= entries.Count || index1 == index2)
            return false;

        entries[index1].Index = index2;
        entries[index2].Index = index1;
        
        await uow.SaveChangesAsync();

        return true;
    }

    public async Task<bool> MoveEntryAsync(ulong guildId, int fromIndex, int toIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fromIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(toIndex);

        await using var uow = _db.GetDbContext();
        var entries = await GetEntriesInternal(uow, guildId);

        if (fromIndex >= entries.Count || toIndex >= entries.Count || fromIndex == toIndex)
            return false;

        var entry = entries[fromIndex];
        entries.RemoveAt(fromIndex);
        entries.Insert(toIndex, entry);

        await uow.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetItemRoleRequirementAsync(ulong guildId, int index, ulong? roleId)
    {
        await using var uow = _db.GetDbContext();

        var changes = await uow.GetTable<ShopEntry>()
                               .Where(x => x.GuildId == guildId && x.Index == index)
                               .UpdateAsync(x => new ShopEntry()
                               {
                                   RoleRequirement = roleId,
                               });
        return changes > 0;
    }

    public async Task<ShopEntry> AddShopCommandAsync(
        ulong guildId,
        ulong userId,
        int price,
        string command)
    {
        await using var uow = _db.GetDbContext();
        var entry = await uow.GetTable<ShopEntry>()
                             .InsertWithOutputAsync(() => new()
                             {
                                 AuthorId = userId,
                                 Command = command,
                                 Type = ShopEntryType.Command,
                                 Price = price,
                             });

        return entry;
    }
}