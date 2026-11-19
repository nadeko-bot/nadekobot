using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Modules.Games.SavedGames;
using NadekoBot.Services.Currency;

namespace NadekoBot.Modules.Gambling.Common.AnimalRacing;

public static class AnimalRaceTx
{
    public const string TYPE = "animalrace";
    public const string BET = "bet";
    public const string WIN = "win";
    public const string REFUND = "refund";
}

public enum RaceJoinResult : byte
{
    Joined,
    AlreadyJoined,
    AlreadyStarted,
    Full,
    NotEnough,
    NoRace
}

public readonly record struct RaceSettlement(bool Settled, long Payout);

// The bets and the race row change in one transaction, so a race which runs again after a restart pays one time.
public sealed class AnimalRaceLedger(
    DbService db,
    ITxTracker txTracker,
    IBotStrings strings,
    TimeProvider? timeProvider = null) : INService
{
    public const int MAX_ORPHANS_PER_CALL = 500;

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<RaceJoinResult> JoinAsync(ulong guildId, ulong channelId, ulong userId, long amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        var txData = new TxData(AnimalRaceTx.TYPE, AnimalRaceTx.BET);
        var now = _time.GetUtcNow().UtcDateTime;

        await using (var ctx = db.GetDbContext())
        {
            await using var tx = await ctx.Database.BeginTransactionAsync();

            if (amount > 0 && !await DefaultWallet.TakeAsync(ctx, userId, amount, txData))
                return RaceJoinResult.NotEnough;

            // the race row is the source of the insert, so nobody joins a race which already ended
            var joined = await ctx.GetTable<SavedGame>()
                .Where(g => g.GuildId == guildId
                            && g.ChannelId == channelId
                            && g.Kind == SavedGameKind.AnimalRace
                            && !ctx.GetTable<AnimalRaceEntry>().Any(e => e.GuildId == guildId && e.UserId == userId))
                .InsertAsync(ctx.GetTable<AnimalRaceEntry>(),
                    _ => new AnimalRaceEntry
                    {
                        GuildId = guildId,
                        ChannelId = channelId,
                        UserId = userId,
                        Amount = amount,
                        DateAdded = now
                    });

            if (joined == 0)
            {
                var already = await ctx.GetTable<AnimalRaceEntry>()
                    .AnyAsyncLinqToDB(e => e.GuildId == guildId && e.UserId == userId);

                return already ? RaceJoinResult.AlreadyJoined : RaceJoinResult.NoRace;
            }

            await tx.CommitAsync();
        }

        if (amount > 0)
        {
            try
            {
                await txTracker.TrackRemove(userId, amount, txData);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Animal race could not track a bet of {Amount} from {UserId}", amount, userId);
            }
        }

        return RaceJoinResult.Joined;
    }

    // Pays only when the race row is still there, so a second call pays nothing.
    public async Task<RaceSettlement> SettleAsync(
        int raceId,
        ulong guildId,
        ulong channelId,
        ulong winnerId,
        double multiplier)
    {
        var tracked = new List<TrackedAdd>(1);
        long payout = 0;
        var settled = false;

        await using (var ctx = db.GetDbContext())
        {
            await using var tx = await ctx.Database.BeginTransactionAsync();

            // only the call which deletes the race row touches the entries, so a late retry cannot reach a newer race
            var deleted = await ctx.GetTable<SavedGame>()
                .Where(x => x.Id == raceId)
                .DeleteAsync();

            if (deleted == 0)
                return new(false, 0);

            var entries = await ctx.GetTable<AnimalRaceEntry>()
                .Where(x => x.GuildId == guildId && x.ChannelId == channelId)
                .DeleteWithOutputAsync();

            AnimalRaceEntry? winner = null;
            foreach (var entry in entries)
            {
                if (entry.UserId == winnerId)
                    winner = entry;
            }

            if (winner is null)
            {
                if (entries.Length > 0)
                {
                    Log.Error("The winner {UserId} of the animal race in {ChannelId} has no entry. The bot refunds the race",
                        winnerId,
                        channelId);

                    await AddRefundsInternalAsync(ctx, entries, tracked);
                }
            }
            else
            {
                settled = true;
                payout = (long)(winner.Amount * multiplier);
                if (payout > 0)
                {
                    var txData = new TxData(AnimalRaceTx.TYPE, AnimalRaceTx.WIN);
                    await DefaultWallet.AddAsync(ctx, winnerId, payout, txData);
                    tracked.Add(new(winnerId, payout, txData));
                }
            }

            await tx.CommitAsync();
        }

        await TrackInternalAsync(tracked);
        return new(settled, settled ? payout : 0);
    }

    public async Task<int> RefundAsync(int raceId, ulong guildId, ulong channelId)
    {
        var tracked = new List<TrackedAdd>();

        await using (var ctx = db.GetDbContext())
        {
            await using var tx = await ctx.Database.BeginTransactionAsync();

            var deleted = await ctx.GetTable<SavedGame>()
                .Where(x => x.Id == raceId)
                .DeleteAsync();

            if (deleted == 0)
                return 0;

            var entries = await ctx.GetTable<AnimalRaceEntry>()
                .Where(x => x.GuildId == guildId && x.ChannelId == channelId)
                .DeleteWithOutputAsync();

            await AddRefundsInternalAsync(ctx, entries, tracked);
            await tx.CommitAsync();
        }

        await TrackInternalAsync(tracked);
        return tracked.Count;
    }

    // Refunds the entries of this shard which belong to no race row.
    public async Task<int> RefundOrphansAsync(int totalShards, int shardId)
    {
        var refunded = 0;
        while (true)
        {
            var tracked = new List<TrackedAdd>();
            int found;

            await using (var ctx = db.GetDbContext())
            {
                var ids = await ctx.GetTable<AnimalRaceEntry>()
                    .Where(Queries.GuildOnShard<AnimalRaceEntry>(x => x.GuildId, totalShards, shardId))
                    .Where(x => !ctx.GetTable<SavedGame>()
                        .Any(g => g.GuildId == x.GuildId
                                  && g.ChannelId == x.ChannelId
                                  && g.Kind == SavedGameKind.AnimalRace))
                    .OrderBy(x => x.Id)
                    .Take(MAX_ORPHANS_PER_CALL)
                    .Select(x => x.Id)
                    .ToListAsyncLinqToDB();

                found = ids.Count;
                if (found == 0)
                    return refunded;

                await using var tx = await ctx.Database.BeginTransactionAsync();

                var entries = await ctx.GetTable<AnimalRaceEntry>()
                    .Where(x => ids.Contains(x.Id))
                    .Where(x => !ctx.GetTable<SavedGame>()
                        .Any(g => g.GuildId == x.GuildId
                                  && g.ChannelId == x.ChannelId
                                  && g.Kind == SavedGameKind.AnimalRace))
                    .DeleteWithOutputAsync();

                await AddRefundsInternalAsync(ctx, entries, tracked);
                await tx.CommitAsync();
            }

            await TrackInternalAsync(tracked);
            refunded += tracked.Count;

            if (found < MAX_ORPHANS_PER_CALL)
                return refunded;
        }
    }

    public async Task<AnimalRaceEntry[]> GetEntriesAsync(ulong guildId, ulong channelId)
    {
        await using var ctx = db.GetDbContext();
        return await ctx.GetTable<AnimalRaceEntry>()
            .Where(x => x.GuildId == guildId && x.ChannelId == channelId)
            .OrderBy(x => x.Id)
            .ToArrayAsyncLinqToDB();
    }

    private async Task AddRefundsInternalAsync(NadekoContext ctx, AnimalRaceEntry[] entries, List<TrackedAdd> tracked)
    {
        foreach (var entry in entries)
        {
            if (entry.Amount <= 0)
                continue;

            var note = strings.GetText(strs.animal_race_note_refund, entry.GuildId);
            var txData = new TxData(AnimalRaceTx.TYPE, AnimalRaceTx.REFUND, note);
            await DefaultWallet.AddAsync(ctx, entry.UserId, entry.Amount, txData);
            tracked.Add(new(entry.UserId, entry.Amount, txData));
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
                Log.Warning(ex, "Animal race could not track a payout of {Amount} to {UserId}", t.Amount, t.UserId);
            }
        }
    }

    private readonly record struct TrackedAdd(ulong UserId, long Amount, TxData Data);
}
