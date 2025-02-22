using NadekoBot.Db.Models;
using NadekoBot.Services.Currency;

namespace NadekoBot.Services;

public interface ICurrencyService
{
    Task<IWallet> GetWalletAsync(ulong userId, CurrencyType type = CurrencyType.Default);

    Task AddBulkAsync(
        IReadOnlyCollection<ulong> userIds,
        long amount,
        TxData? txData,
        CurrencyType type = CurrencyType.Default);

    Task RemoveBulkAsync(
        IReadOnlyCollection<ulong> userIds,
        long amount,
        TxData? txData,
        CurrencyType type = CurrencyType.Default);

    Task AddAsync(
        ulong userId,
        long amount,
        TxData? txData);

    Task AddAsync(
        IUser user,
        long amount,
        TxData? txData);

    Task<bool> RemoveAsync(
        ulong userId,
        long amount,
        TxData? txData);

    Task<bool> RemoveAsync(
        IUser user,
        long amount,
        TxData? txData);
    
    Task<IReadOnlyList<DiscordUser>> GetTopRichest(ulong ignoreId, int page = 0, int perPage = 9);

    Task<IReadOnlyList<CurrencyTransaction>> GetTransactionsAsync(
        ulong userId,
        int page,
        int perPage = 15);
    
    Task<int> GetTransactionsCountAsync(ulong userId);
}