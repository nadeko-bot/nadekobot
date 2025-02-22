#nullable disable
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Gambling.Services;

public interface IShopService
{
    /// <summary>
    ///     Changes the price of a shop item
    /// </summary>
    /// <param name="guildId">Id of the guild in which the shop is</param>
    /// <param name="index">Index of the item</param>
    /// <param name="newPrice">New item price</param>
    /// <returns>Success status</returns>
    Task<bool> ChangeEntryPriceAsync(ulong guildId, int index, int newPrice);

    /// <summary>
    ///     Changes the name of a shop item
    /// </summary>
    /// <param name="guildId">Id of the guild in which the shop is</param>
    /// <param name="index">Index of the item</param>
    /// <param name="newName">New item name</param>
    /// <returns>Success status</returns>
    Task<bool> ChangeEntryNameAsync(ulong guildId, int index, string newName);

    /// <summary>
    ///     Swaps indexes of 2 items in the shop
    /// </summary>
    /// <param name="guildId">Id of the guild in which the shop is</param>
    /// <param name="index1">First entry's index</param>
    /// <param name="index2">Second entry's index</param>
    /// <returns>Whether swap was successful</returns>
    Task<bool> SwapEntriesAsync(ulong guildId, int index1, int index2);

    /// <summary>
    ///     Swaps indexes of 2 items in the shop
    /// </summary>
    /// <param name="guildId">Id of the guild in which the shop is</param>
    /// <param name="fromIndex">Current index of the entry to move</param>
    /// <param name="toIndex">Destination index of the entry</param>
    /// <returns>Whether swap was successful</returns>
    Task<bool> MoveEntryAsync(ulong guildId, int fromIndex, int toIndex);

    Task<bool> SetItemRoleRequirementAsync(ulong guildId, int index, ulong? roleId);
    Task<ShopEntry> AddShopCommandAsync(ulong guildId, ulong userId, int price, string command);
}