#nullable disable
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Db.Models;
using NadekoBot.Services.Currency;

namespace NadekoBot.Services;

public sealed class CurrencyService : ICurrencyService, INService
{
    private readonly DbService _db;
    private readonly ITxTracker _txTracker;

    public CurrencyService(DbService db, ITxTracker txTracker)
    {
        _db = db;
        _txTracker = txTracker;
    }

    public Task<IWallet> GetWalletAsync(ulong userId, CurrencyType type = CurrencyType.Default)
    {
        if (type == CurrencyType.Default)
            return Task.FromResult<IWallet>(new DefaultWallet(userId, _db));

        throw new ArgumentOutOfRangeException(nameof(type));
    }

    public async Task AddBulkAsync(
        IReadOnlyCollection<ulong> userIds,
        long amount,
        TxData txData,
        CurrencyType type = CurrencyType.Default)
    {
        if (type == CurrencyType.Default)
        {
            foreach (var userId in userIds)
            {
                var wallet = await GetWalletAsync(userId);
                await wallet.Add(amount, txData);
            }

            return;
        }

        throw new ArgumentOutOfRangeException(nameof(type));
    }

    public async Task RemoveBulkAsync(
        IReadOnlyCollection<ulong> userIds,
        long amount,
        TxData txData,
        CurrencyType type = CurrencyType.Default)
    {
        if (type == CurrencyType.Default)
        {
            await using var ctx = _db.GetDbContext();
            await ctx
                  .GetTable<DiscordUser>()
                  .Where(x => userIds.Contains(x.UserId))
                  .UpdateAsync(du => new()
                  {
                      CurrencyAmount = du.CurrencyAmount >= amount
                          ? du.CurrencyAmount - amount
                          : 0
                  });
            await ctx.SaveChangesAsync();
            return;
        }

        throw new ArgumentOutOfRangeException(nameof(type));
    }

    public async Task AddAsync(
        ulong userId,
        long amount,
        TxData txData)
    {
        var wallet = await GetWalletAsync(userId);
        await wallet.Add(amount, txData);
        await _txTracker.TrackAdd(userId, amount, txData);
    }

    public async Task AddAsync(
        IUser user,
        long amount,
        TxData txData)
        => await AddAsync(user.Id, amount, txData);

    public async Task<bool> RemoveAsync(
        ulong userId,
        long amount,
        TxData txData)
    {
        if (amount == 0)
            return true;

        var wallet = await GetWalletAsync(userId);
        var result = await wallet.Take(amount, txData);
        if (result)
            await _txTracker.TrackRemove(userId, amount, txData);
        return result;
    }

    public async Task<bool> RemoveAsync(
        IUser user,
        long amount,
        TxData txData)
        => await RemoveAsync(user.Id, amount, txData);

    public async Task<IReadOnlyList<DiscordUser>> GetTopRichest(ulong ignoreId, int page = 0, int perPage = 9)
    {
        await using var uow = _db.GetDbContext();
        return await uow.Set<DiscordUser>().GetTopRichest(ignoreId, page, perPage);
    }

    public async Task<IReadOnlyList<CurrencyTransaction>> GetTransactionsAsync(
        ulong userId,
        int page,
        int perPage = 15)
    {
        await using var uow = _db.GetDbContext();

        var trs = await uow.GetTable<CurrencyTransaction>()
                           .Where(x => x.UserId == userId)
                           .OrderByDescending(x => x.DateAdded)
                           .Skip(perPage * page)
                           .Take(perPage)
                           .ToListAsyncLinqToDB();

        return trs;
    }

    public async Task<int> GetTransactionsCountAsync(ulong userId)
    {
        await using var uow = _db.GetDbContext();
        return await uow.GetTable<CurrencyTransaction>()
                       .Where(x => x.UserId == userId)
                       .CountAsyncLinqToDB();
    }
}