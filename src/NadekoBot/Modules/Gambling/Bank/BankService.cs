using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Games.Quests;

namespace NadekoBot.Modules.Gambling.Bank;

public sealed class BankService(
    ICurrencyService _cur,
    DbService _db,
    QuestService quests) : IBankService, INService
{
    public async Task<bool> AwardAsync(ulong userId, long amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        await using var ctx = _db.GetDbContext();
        await ctx.GetTable<BankUser>()
            .InsertOrUpdateAsync(() => new()
                {
                    UserId = userId,
                    Balance = amount
                },
                (old) => new()
                {
                    Balance = old.Balance + amount
                },
                () => new()
                {
                    UserId = userId
                });

        return true;
    }

    public async Task<bool> TakeAsync(ulong userId, long amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        await using var ctx = _db.GetDbContext();
        var rows = await ctx.Set<BankUser>()
            .ToLinqToDBTable()
            .Where(x => x.UserId == userId && x.Balance >= amount)
            .UpdateAsync((old) => new()
            {
                Balance = old.Balance - amount
            });

        return rows > 0;
    }

    public async Task<bool> DepositAsync(ulong userId, long amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        if (!await _cur.RemoveAsync(userId, amount, new("bank", "deposit")))
            return false;

        await using var ctx = _db.GetDbContext();
        await ctx.Set<BankUser>()
            .ToLinqToDBTable()
            .InsertOrUpdateAsync(() => new()
                {
                    UserId = userId,
                    Balance = amount
                },
                (old) => new()
                {
                    Balance = old.Balance + amount
                },
                () => new()
                {
                    UserId = userId
                });

        await quests.ReportActionAsync(userId,
            QuestEventType.BankAction,
            new()
            {
                { "type", "deposit" },
                { "amount", amount.ToString() }
            });

        return true;
    }

    public async Task<bool> WithdrawAsync(ulong userId, long amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        await using var ctx = _db.GetDbContext();
        var rows = await ctx.Set<BankUser>()
            .ToLinqToDBTable()
            .Where(x => x.UserId == userId && x.Balance >= amount)
            .UpdateAsync((old) => new()
            {
                Balance = old.Balance - amount
            });

        if (rows > 0)
        {
            await _cur.AddAsync(userId, amount, new("bank", "withdraw"));
            await quests.ReportActionAsync(userId,
                QuestEventType.BankAction,
                new()
                {
                    { "type", "withdraw" },
                    { "amount", amount.ToString() }
                });
            return true;
        }

        return false;
    }

    public async Task<long> GetBalanceAsync(ulong userId)
    {
        await using var ctx = _db.GetDbContext();
        var res = (await ctx.Set<BankUser>()
                      .ToLinqToDBTable()
                      .FirstOrDefaultAsync(x => x.UserId == userId))
                  ?.Balance
                  ?? 0;

        await quests.ReportActionAsync(userId,
            QuestEventType.BankAction,
            new()
            {
                { "type", "balance" },
            });
        return res;
    }
}