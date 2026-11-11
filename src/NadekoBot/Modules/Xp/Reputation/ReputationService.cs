using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Xp;

public readonly record struct GiveRepResult(bool Given, int Reputation, DateTime NextGiveAt);

public readonly record struct RepInfo(int Reputation, DateTime? NextGiveAt);

public readonly record struct RepLbEntry(ulong UserId, int Reputation);

public sealed class ReputationService(DbService db, TimeProvider? timeProvider = null) : INService
{
    public const int LB_PAGE_SIZE = 10;
    public static readonly TimeSpan GiveCooldown = TimeSpan.FromHours(24);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<GiveRepResult> GiveAsync(ulong guildId, ulong giverId, ulong receiverId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(giverId, receiverId);

        var now = _time.GetUtcNow().UtcDateTime;
        var cutoff = now - GiveCooldown;

        await using var ctx = db.GetDbContext();
        var table = ctx.GetTable<UserReputation>();

        // a failure after the cooldown claim must not use up the giver's cooldown
        await using var tx = await ctx.Database.BeginTransactionAsync();

        var claimed = await table
                            .Where(x => x.GuildId == guildId
                                        && x.UserId == giverId
                                        && (x.LastGivenAt == null || x.LastGivenAt <= cutoff))
                            .Set(x => x.LastGivenAt, now)
                            .UpdateAsync();

        if (claimed == 0)
        {
            // a null update setter inserts only when the giver has no row yet
            claimed = await table.InsertOrUpdateAsync(() => new()
                {
                    GuildId = guildId,
                    UserId = giverId,
                    Reputation = 0,
                    LastGivenAt = now,
                },
                null,
                () => new()
                {
                    GuildId = guildId,
                    UserId = giverId,
                });
        }

        if (claimed == 0)
        {
            var lastGiven = await table
                                  .Where(x => x.GuildId == guildId && x.UserId == giverId)
                                  .Select(x => x.LastGivenAt)
                                  .FirstOrDefaultAsyncLinqToDB();

            return new(false, 0, (lastGiven ?? now) + GiveCooldown);
        }

        await table.InsertOrUpdateAsync(() => new()
            {
                GuildId = guildId,
                UserId = receiverId,
                Reputation = 1,
            },
            old => new()
            {
                Reputation = old.Reputation + 1,
            },
            () => new()
            {
                GuildId = guildId,
                UserId = receiverId,
            });

        var reputation = await table
                               .Where(x => x.GuildId == guildId && x.UserId == receiverId)
                               .Select(x => x.Reputation)
                               .FirstOrDefaultAsyncLinqToDB();

        await tx.CommitAsync();

        return new(true, reputation, now + GiveCooldown);
    }

    public async Task<RepInfo> GetAsync(ulong guildId, ulong userId)
    {
        await using var ctx = db.GetDbContext();

        var row = await ctx.GetTable<UserReputation>()
                           .Where(x => x.GuildId == guildId && x.UserId == userId)
                           .Select(x => new { x.Reputation, x.LastGivenAt })
                           .FirstOrDefaultAsyncLinqToDB();

        if (row is null)
            return new(0, null);

        var next = row.LastGivenAt + GiveCooldown;
        return new(row.Reputation, next > _time.GetUtcNow().UtcDateTime ? next : null);
    }

    public async Task<int> CountLeaderboardAsync(ulong guildId)
    {
        await using var ctx = db.GetDbContext();

        return await ctx.GetTable<UserReputation>()
                        .Where(x => x.GuildId == guildId && x.Reputation > 0)
                        .CountAsyncLinqToDB();
    }

    public async Task<IReadOnlyCollection<RepLbEntry>> GetLeaderboardAsync(ulong guildId, int page)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(page);

        await using var ctx = db.GetDbContext();

        return await ctx.GetTable<UserReputation>()
                        .Where(x => x.GuildId == guildId && x.Reputation > 0)
                        .OrderByDescending(x => x.Reputation)
                        .ThenBy(x => x.Id)
                        .Skip(page * LB_PAGE_SIZE)
                        .Take(LB_PAGE_SIZE)
                        .Select(x => new RepLbEntry(x.UserId, x.Reputation))
                        .ToArrayAsyncLinqToDB();
    }
}
