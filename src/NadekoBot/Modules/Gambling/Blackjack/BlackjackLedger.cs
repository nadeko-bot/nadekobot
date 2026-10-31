using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Games.Quests;
using NadekoBot.Services.Currency;

namespace NadekoBot.Modules.Gambling.Blackjack;

public static class BlackjackTx
{
    public const string TYPE = "blackjack";

    public const string BET = "bet";
    public const string SPLIT = "split";
    public const string DOUBLE = "double";

    public const string WIN = "win";
    public const string BLACKJACK = "blackjack";
    public const string PUSH = "push";

    public const string BET_REFUND = "bet refund";
    public const string SPLIT_REFUND = "split refund";
    public const string DOUBLE_REFUND = "double refund";

    private const string REFUND_SUFFIX = " refund";

    public static string StakeExtra(BjStakeKind kind)
        => kind switch
        {
            BjStakeKind.Split => SPLIT,
            BjStakeKind.Double => DOUBLE,
            _ => BET
        };

    public static string RefundExtra(BjStakeKind kind)
        => kind switch
        {
            BjStakeKind.Split => SPLIT_REFUND,
            BjStakeKind.Double => DOUBLE_REFUND,
            _ => BET_REFUND
        };

    public static bool IsRefund(string extra)
        => extra.EndsWith(REFUND_SUFFIX, StringComparison.Ordinal);
}

public enum BjSeatResult : byte
{
    Seated,
    AlreadySeated,
    NotEnough
}

public enum BjRefundReason : byte
{
    Restart,
    Stopped,
    Mismatch,
    Unpaid
}

public readonly record struct BjSettlement(ulong UserId, long ExpectedStake, List<BlackjackPayout> Payouts);

// Every currency change of a round and its stake rows change in one database transaction.
public sealed class BlackjackLedger(
    DbService db,
    ITxTracker txTracker,
    QuestService quests,
    IBotStrings strings,
    TimeProvider? timeProvider = null) : INService
{
    // old stakes which one call reads at most, so one statement and one write transaction stay small
    public const int MAX_STALE_PER_CALL = 500;

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<BjSeatResult> SeatAsync(ulong guildId, ulong channelId, ulong userId, long bet)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bet);

        var txData = new TxData(BlackjackTx.TYPE, BlackjackTx.BET);
        var now = _time.GetUtcNow().UtcDateTime;

        await using (var ctx = db.GetDbContext())
        {
            await using var tx = await ctx.Database.BeginTransactionAsync();

            if (!await DefaultWallet.TakeAsync(ctx, userId, bet, txData))
                return BjSeatResult.NotEnough;

            // a member with any stake sits at a table, also at one on another shard
            var seated = await ctx.GetTable<DiscordUser>()
                .Where(u => u.UserId == userId && !ctx.GetTable<BlackjackStake>().Any(s => s.UserId == userId))
                .InsertAsync(ctx.GetTable<BlackjackStake>(),
                    _ => new BlackjackStake
                    {
                        GuildId = guildId,
                        ChannelId = channelId,
                        UserId = userId,
                        Kind = BjStakeKind.Bet,
                        Amount = bet,
                        DateAdded = now
                    });

            if (seated == 0)
                return BjSeatResult.AlreadySeated;

            await tx.CommitAsync();
        }

        await TrackRemoveInternalAsync(userId, bet, txData);
        return BjSeatResult.Seated;
    }

    public async Task<bool> StakeAsync(ulong guildId, ulong channelId, ulong userId, long amount, BjStakeKind kind)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        var txData = new TxData(BlackjackTx.TYPE, BlackjackTx.StakeExtra(kind));
        var now = _time.GetUtcNow().UtcDateTime;

        await using (var ctx = db.GetDbContext())
        {
            await using var tx = await ctx.Database.BeginTransactionAsync();

            if (!await DefaultWallet.TakeAsync(ctx, userId, amount, txData))
                return false;

            await ctx.GetTable<BlackjackStake>()
                .InsertAsync(() => new BlackjackStake
                {
                    GuildId = guildId,
                    ChannelId = channelId,
                    UserId = userId,
                    Kind = kind,
                    Amount = amount,
                    DateAdded = now
                });

            await tx.CommitAsync();
        }

        await TrackRemoveInternalAsync(userId, amount, txData);
        return true;
    }

    // One transaction for the whole round. A player whose stakes are gone is skipped, so a second call pays nothing;
    // a player whose stakes do not add up gets them refunded, and the other players are paid normally.
    public async Task<int> SettleRoundAsync(
        ulong guildId,
        ulong channelId,
        IReadOnlyList<BjSettlement> players,
        BjRefundReason? refundReason = null)
    {
        var userIds = new ulong[players.Count];
        for (var i = 0; i < userIds.Length; i++)
            userIds[i] = players[i].UserId;

        var tracked = new List<TrackedAdd>(players.Count * 2);
        // only a round which the players finished counts as bets for quests; a refunded stake is no bet
        var bets = refundReason is null ? new List<(ulong UserId, long Amount)>(players.Count) : null;
        var settled = 0;

        await using (var ctx = db.GetDbContext())
        {
            await using var tx = await ctx.Database.BeginTransactionAsync();

            // The saved round and its stakes leave in one transaction, so a paid round cannot be played again.
            await ctx.GetTable<BlackjackRoundState>()
                     .Where(x => x.GuildId == guildId && x.ChannelId == channelId)
                     .DeleteAsync();

            var stakes = await ctx.GetTable<BlackjackStake>()
                .Where(x => x.GuildId == guildId && x.ChannelId == channelId && userIds.Contains(x.UserId))
                .DeleteWithOutputAsync();

            if (stakes.Length == 0)
            {
                await tx.CommitAsync();
                return 0;
            }

            var refundNote = refundReason is { } reason ? GetNoteInternal(reason, guildId) : null;
            foreach (var player in players)
            {
                var found = false;
                long staked = 0;
                foreach (var stake in stakes)
                {
                    if (stake.UserId != player.UserId)
                        continue;

                    found = true;
                    staked += stake.Amount;
                }

                if (!found)
                    continue;

                settled++;
                if (staked != player.ExpectedStake)
                {
                    Log.Error("Blackjack stakes of {UserId} in {ChannelId} are {Staked}, but the round has {Expected}. "
                              + "The bot refunds the stakes",
                        player.UserId,
                        channelId,
                        staked,
                        player.ExpectedStake);

                    await AddRefundsInternalAsync(ctx, stakes, player.UserId, BjRefundReason.Mismatch, tracked);
                    continue;
                }

                foreach (var payout in player.Payouts)
                {
                    if (payout.Amount <= 0)
                        continue;

                    var txData = BlackjackTx.IsRefund(payout.Extra) && refundNote is not null
                        ? new TxData(BlackjackTx.TYPE, payout.Extra, refundNote)
                        : new TxData(BlackjackTx.TYPE, payout.Extra);

                    await DefaultWallet.AddAsync(ctx, player.UserId, payout.Amount, txData);
                    tracked.Add(new(player.UserId, payout.Amount, txData));
                }

                bets?.Add((player.UserId, staked));
            }

            await tx.CommitAsync();
        }

        await TrackInternalAsync(tracked);

        if (bets is not null)
        {
            foreach (var (userId, amount) in bets)
            {
                try
                {
                    await quests.ReportActionAsync(userId,
                        QuestEventType.BetPlaced,
                        new()
                        {
                            { "type", BlackjackTx.TYPE },
                            { "amount", amount.ToString() }
                        });
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Blackjack could not report the bet of {UserId} to quests", userId);
                }
            }
        }

        return settled;
    }

    public async Task<long> RefundAsync(ulong guildId, ulong channelId, ulong userId, BjRefundReason reason)
    {
        var tracked = new List<TrackedAdd>(1);

        await using (var ctx = db.GetDbContext())
        {
            await using var tx = await ctx.Database.BeginTransactionAsync();

            var stakes = await ctx.GetTable<BlackjackStake>()
                .Where(x => x.GuildId == guildId && x.ChannelId == channelId && x.UserId == userId)
                .DeleteWithOutputAsync();

            if (stakes.Length == 0)
                return 0;

            await AddRefundsInternalAsync(ctx, stakes, null, reason, tracked);
            await tx.CommitAsync();
        }

        await TrackInternalAsync(tracked);

        long total = 0;
        foreach (var t in tracked)
            total += t.Amount;
        return total;
    }

    // The table is settled in one transaction with its stakes, so a saved round is never paid twice.
    public async Task SaveRoundAsync(ulong guildId, ulong channelId, byte[] cards)
    {
        var now = _time.GetUtcNow().UtcDateTime;

        await using var ctx = db.GetDbContext();
        await ctx.GetTable<BlackjackRoundState>()
                 .InsertOrUpdateAsync(
                     () => new BlackjackRoundState
                     {
                         GuildId = guildId,
                         ChannelId = channelId,
                         Cards = cards,
                         DateAdded = now
                     },
                     _ => new BlackjackRoundState { Cards = cards, DateAdded = now },
                     () => new BlackjackRoundState { ChannelId = channelId });
    }

    public async Task<BlackjackRoundState[]> GetRoundStatesAsync(int totalShards, int shardId)
    {
        await using var ctx = db.GetDbContext();
        return await ctx.GetTable<BlackjackRoundState>()
                        .Where(Queries.GuildOnShard<BlackjackRoundState>(x => x.GuildId, totalShards, shardId))
                        .ToArrayAsyncLinqToDB();
    }

    // Only a saved round which cannot be read is deleted without a payout; the stale sweep then refunds its stakes.
    public async Task DeleteRoundAsync(ulong guildId, ulong channelId)
    {
        await using var ctx = db.GetDbContext();
        await ctx.GetTable<BlackjackRoundState>()
                 .Where(x => x.GuildId == guildId && x.ChannelId == channelId)
                 .DeleteAsync();
    }

    // Refunds the stakes of this shard which are older than the cutoff, except those of members who sit at a table:
    // a live round always holds the seats of its players, so it is never touched. A round which the bot saved
    // is finished instead, so its stakes wait for that.
    public async Task<int> RefundStaleAsync(
        int totalShards,
        int shardId,
        DateTime before,
        BjRefundReason reason,
        Func<ulong, bool> isSeated)
    {
        var tracked = new List<TrackedAdd>();

        await using (var ctx = db.GetDbContext())
        {
            // read and filter outside the transaction, so the write lock of all shards covers only the delete
            var candidates = await ctx.GetTable<BlackjackStake>()
                .Where(Queries.GuildOnShard<BlackjackStake>(x => x.GuildId, totalShards, shardId))
                .Where(x => x.DateAdded < before)
                .Where(x => !ctx.GetTable<BlackjackRoundState>()
                                .Any(r => r.GuildId == x.GuildId && r.ChannelId == x.ChannelId))
                .OrderBy(x => x.Id)
                .Take(MAX_STALE_PER_CALL)
                .Select(x => new { x.Id, x.UserId })
                .ToListAsyncLinqToDB();

            if (candidates.Count == MAX_STALE_PER_CALL)
            {
                Log.Warning("Blackjack found {Count} or more old stakes on shard {ShardId}. The next check refunds the rest",
                    MAX_STALE_PER_CALL,
                    shardId);
            }

            var ids = new List<int>(candidates.Count);
            foreach (var candidate in candidates)
            {
                if (!isSeated(candidate.UserId))
                    ids.Add(candidate.Id);
            }

            if (ids.Count == 0)
                return 0;

            await using var tx = await ctx.Database.BeginTransactionAsync();

            var stakes = await ctx.GetTable<BlackjackStake>()
                .Where(x => ids.Contains(x.Id) && x.DateAdded < before)
                .Where(x => !ctx.GetTable<BlackjackRoundState>()
                                .Any(r => r.GuildId == x.GuildId && r.ChannelId == x.ChannelId))
                .DeleteWithOutputAsync();

            if (stakes.Length == 0)
                return 0;

            await AddRefundsInternalAsync(ctx, stakes, null, reason, tracked);
            await tx.CommitAsync();
        }

        await TrackInternalAsync(tracked);
        return tracked.Count;
    }

    private async Task AddRefundsInternalAsync(
        NadekoContext ctx,
        BlackjackStake[] stakes,
        ulong? onlyUserId,
        BjRefundReason reason,
        List<TrackedAdd> tracked)
    {
        foreach (var stake in stakes)
        {
            if (stake.Amount <= 0 || (onlyUserId is { } userId && stake.UserId != userId))
                continue;

            var txData = new TxData(BlackjackTx.TYPE, BlackjackTx.RefundExtra(stake.Kind), GetNoteInternal(reason, stake.GuildId));
            await DefaultWallet.AddAsync(ctx, stake.UserId, stake.Amount, txData);
            tracked.Add(new(stake.UserId, stake.Amount, txData));
        }
    }

    // The note is written in the language of the server, because the transaction keeps it as text.
    private string GetNoteInternal(BjRefundReason reason, ulong guildId)
    {
        var str = reason switch
        {
            BjRefundReason.Restart => strs.bj_note_restart,
            BjRefundReason.Stopped => strs.bj_note_stopped,
            BjRefundReason.Unpaid => strs.bj_note_unpaid,
            _ => strs.bj_note_mismatch
        };

        return strings.GetText(str.Key, guildId, str.Params);
    }

    // the currency already moved, so a stats failure must not look like a failed stake
    private async Task TrackRemoveInternalAsync(ulong userId, long amount, TxData txData)
    {
        try
        {
            await txTracker.TrackRemove(userId, amount, txData);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Blackjack could not track a stake of {Amount} from {UserId}", amount, userId);
        }
    }

    private async Task TrackInternalAsync(List<TrackedAdd> tracked)
    {
        foreach (var t in tracked)
        {
            try
            {
                await txTracker.TrackAdd(t.UserId, t.Amount, t.Data);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Blackjack could not track a payout of {Amount} to {UserId}", t.Amount, t.UserId);
            }
        }
    }

    private readonly record struct TrackedAdd(ulong UserId, long Amount, TxData Data);
}
