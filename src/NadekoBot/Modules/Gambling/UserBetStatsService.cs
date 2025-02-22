#nullable disable
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Gambling.Services;

public sealed class UserBetStatsService : INService
{
    private const long RESET_MIN_PRICE = 1000;
    private const decimal RESET_TOTAL_MULTIPLIER = 0.002m;

    private readonly DbService _db;
    private readonly ICurrencyService _cs;

    public UserBetStatsService(DbService db, ICurrencyService cs)
    {
        _db = db;
        _cs = cs;
    }

    public async Task<long> GetResetStatsPriceAsync(ulong userId, GamblingGame? game)
    {
        await using var ctx = _db.GetDbContext();

        var totalBet = await ctx.GetTable<UserBetStats>()
                                .Where(x => x.UserId == userId && (game == null || x.Game == game))
                                .SumAsyncLinqToDB(x => x.TotalBet);

        return Math.Max(RESET_MIN_PRICE, (long)Math.Ceiling(totalBet * RESET_TOTAL_MULTIPLIER));
    }

    public async Task<bool> ResetStatsAsync(ulong userId, GamblingGame? game)
    {
        var price = await GetResetStatsPriceAsync(userId, game);

        if (!await _cs.RemoveAsync(userId, price, new("betstats", "reset")))
        {
            return false;
        }

        await using var ctx = _db.GetDbContext();
        await ctx.GetTable<UserBetStats>()
                 .DeleteAsync(x => x.UserId == userId && (game == null || x.Game == game));

        return true;
    }

    public async Task ResetGamblingStatsAsync()
    {
        await using var ctx = _db.GetDbContext();
        await ctx.GetTable<GamblingStats>()
                 .DeleteAsync();
    }

    public async Task<IReadOnlyList<UserBetStats>> GetWinLbAsync(int page)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(page);

        await using var ctx = _db.GetDbContext();
        return await ctx.GetTable<UserBetStats>()
                        .OrderByDescending(x => x.MaxWin)
                        .Skip(page * 9)
                        .Take(9)
                        .ToArrayAsyncLinqToDB();
    }
}