﻿using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Games.Quests;
using NadekoBot.Services.Currency;

namespace NadekoBot.Modules.Gambling.Bank;

public sealed class BankService(
    DbService _db,
    ITxTracker _txTracker,
    QuestService quests) : IBankService, INService
{
    private const string TX_TYPE = "bank";
    private const string TX_DEPOSIT = "deposit";
    private const string TX_WITHDRAW = "withdraw";

    public async Task<bool> AwardAsync(ulong userId, long amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        await using var ctx = _db.GetDbContext();
        await AddToBankInternalAsync(ctx, userId, amount);

        return true;
    }

    public async Task<bool> TakeAsync(ulong userId, long amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        await using var ctx = _db.GetDbContext();
        return await TakeFromBankInternalAsync(ctx, userId, amount);
    }

    public async Task<bool> DepositAsync(ulong userId, long amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        var txData = new TxData(TX_TYPE, TX_DEPOSIT);

        // wallet and bank change in one transaction, a failure between the two steps must not destroy currency
        await using (var ctx = _db.GetDbContext())
        {
            await using var tx = await ctx.Database.BeginTransactionAsync();

            if (!await DefaultWallet.TakeAsync(ctx, userId, amount, txData))
                return false;

            await AddToBankInternalAsync(ctx, userId, amount);

            await tx.CommitAsync();
        }

        await _txTracker.TrackRemove(userId, amount, txData);
        await ReportBankActionInternalAsync(userId, TX_DEPOSIT, amount);

        return true;
    }

    public async Task<bool> WithdrawAsync(ulong userId, long amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        var txData = new TxData(TX_TYPE, TX_WITHDRAW);

        await using (var ctx = _db.GetDbContext())
        {
            await using var tx = await ctx.Database.BeginTransactionAsync();

            if (!await TakeFromBankInternalAsync(ctx, userId, amount))
                return false;

            await DefaultWallet.AddAsync(ctx, userId, amount, txData);

            await tx.CommitAsync();
        }

        await _txTracker.TrackAdd(userId, amount, txData);
        await ReportBankActionInternalAsync(userId, TX_WITHDRAW, amount);

        return true;
    }

    public async Task<long> GetBalanceAsync(ulong userId)
    {
        await using var ctx = _db.GetDbContext();
        return await ctx.GetTable<BankUser>()
            .Where(x => x.UserId == userId)
            .Select(x => x.Balance)
            .FirstOrDefaultAsyncLinqToDB();
    }

    public async Task<long> CheckBalanceAsync(ulong userId)
    {
        var balance = await GetBalanceAsync(userId);

        await quests.ReportActionAsync(userId,
            QuestEventType.BankAction,
            new()
            {
                { "type", "balance" },
            });

        return balance;
    }

    private Task ReportBankActionInternalAsync(ulong userId, string type, long amount)
        => quests.ReportActionAsync(userId,
            QuestEventType.BankAction,
            new()
            {
                { "type", type },
                { "amount", amount.ToString() }
            });

    private static Task<int> AddToBankInternalAsync(NadekoContext ctx, ulong userId, long amount)
        => ctx.GetTable<BankUser>()
            .InsertOrUpdateAsync(() => new()
                {
                    UserId = userId,
                    Balance = amount
                },
                old => new()
                {
                    Balance = old.Balance + amount
                },
                () => new()
                {
                    UserId = userId
                });

    private static async Task<bool> TakeFromBankInternalAsync(NadekoContext ctx, ulong userId, long amount)
        => await ctx.GetTable<BankUser>()
            .Where(x => x.UserId == userId && x.Balance >= amount)
            .UpdateAsync(old => new()
            {
                Balance = old.Balance - amount
            }) > 0;
}
