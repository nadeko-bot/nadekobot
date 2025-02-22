using LinqToDB;
using LinqToDB.Data;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Services.Currency;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Gambling;
using System.Collections.Concurrent;

namespace NadekoBot.Services;

public sealed class GamblingTxTracker : ITxTracker, INService, IReadyExecutor
{
    private static readonly IReadOnlySet<string> _gamblingTypes = new HashSet<string>(new[]
    {
        "lula", "betroll", "betflip", "blackjack", "betdraw", "slot",
    });

    private NonBlocking.ConcurrentDictionary<string, (decimal Bet, decimal PaidOut)> globalStats = new();
    private ConcurrentBag<UserBetStats> userStats = new();

    private readonly DbService _db;

    public GamblingTxTracker(DbService db)
    {
        _db = db;
    }

    public async Task OnReadyAsync()
        => await Task.WhenAll(RunUserStatsCollector(), RunBetStatsCollector());

    public async Task RunBetStatsCollector()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync())
        {
            await using var ctx = _db.GetDbContext();

            try
            {
                // update betstats
                var keys = globalStats.Keys;
                foreach (var key in keys)
                {
                    if (globalStats.TryRemove(key, out var stat))
                    {
                        await ctx.GetTable<GamblingStats>()
                                 .InsertOrUpdateAsync(() => new()
                                     {
                                         Feature = key,
                                         Bet = stat.Bet,
                                         PaidOut = stat.PaidOut,
                                         DateAdded = DateTime.UtcNow
                                     },
                                     old => new()
                                     {
                                         Bet = old.Bet + stat.Bet,
                                         PaidOut = old.PaidOut + stat.PaidOut,
                                     },
                                     () => new()
                                     {
                                         Feature = key
                                     });
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "An error occurred in betstats gambling tx tracker");
            }
        }
    }

    private async Task RunUserStatsCollector()
    {
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                if (userStats.Count == 0)
                    continue;

                var users = new List<UserBetStats>(userStats.Count + 5);

                while (userStats.TryTake(out var s))
                    users.Add(s);

                if (users.Count == 0)
                    continue;

                // rakeback
                var rakebacks = new Dictionary<ulong, decimal>();

                // update userstats
                foreach (var (k, x) in users.GroupBy(x => (x.UserId, x.Game))
                                            .ToDictionary(x => x.Key,
                                                x => x.Aggregate((a, b) => new()
                                                {
                                                    WinCount = a.WinCount + b.WinCount,
                                                    LoseCount = a.LoseCount + b.LoseCount,
                                                    TotalBet = a.TotalBet + b.TotalBet,
                                                    PaidOut = a.PaidOut + b.PaidOut,
                                                    MaxBet = Math.Max(a.MaxBet, b.MaxBet),
                                                    MaxWin = Math.Max(a.MaxWin, b.MaxWin),
                                                })))
                {
                    rakebacks.TryAdd(k.UserId, 0m);
                    rakebacks[k.UserId] += x.TotalBet * GetHouseEdge(k.Game) * BASE_RAKEBACK;


                    // bulk upsert in the future
                    await using var uow = _db.GetDbContext();
                    await uow.GetTable<UserBetStats>()
                             .InsertOrUpdateAsync(() => new()
                                 {
                                     UserId = k.UserId,
                                     Game = k.Game,
                                     WinCount = x.WinCount,
                                     LoseCount = Math.Max(0, x.LoseCount),
                                     TotalBet = x.TotalBet,
                                     PaidOut = x.PaidOut,
                                     MaxBet = x.MaxBet,
                                     MaxWin = x.MaxWin
                                 },
                                 o => new()
                                 {
                                     WinCount = o.WinCount + x.WinCount,
                                     LoseCount = Math.Max(0, o.LoseCount + x.LoseCount),
                                     TotalBet = o.TotalBet + x.TotalBet,
                                     PaidOut = o.PaidOut + x.PaidOut,
                                     MaxBet = Math.Max(o.MaxBet, x.MaxBet),
                                     MaxWin = Math.Max(o.MaxWin, x.MaxWin),
                                 },
                                 () => new()
                                 {
                                     UserId = k.UserId,
                                     Game = k.Game
                                 });
                }

                foreach (var (k, v) in rakebacks)
                {
                    await _db.GetDbContext()
                             .GetTable<Rakeback>()
                             .InsertOrUpdateAsync(() => new()
                                 {
                                     UserId = k,
                                     Amount = v
                                 },
                                 (old) => new()
                                 {
                                     Amount = old.Amount + v
                                 },
                                 () => new()
                                 {
                                     UserId = k
                                 });
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "An error occurred in UserBetStats gambling tx tracker");
            }
        }
    }

    private const decimal BASE_RAKEBACK = 0.05m;

    public Task TrackAdd(ulong userId, long amount, TxData? txData)
    {
        if (txData is null)
            return Task.CompletedTask;

        if (_gamblingTypes.Contains(txData.Type))
        {
            globalStats.AddOrUpdate(txData.Type,
                _ => (0, amount),
                (_, old) => (old.Bet, old.PaidOut + amount));
        }

        var mType = GetGameType(txData.Type);

        if (mType is not { } type)
            return Task.CompletedTask;

        if (txData.Type == "lula")
        {
            if (txData.Extra == "lose")
            {
                userStats.Add(new()
                {
                    UserId = userId,
                    Game = type,
                    WinCount = 0,
                    LoseCount = 0,
                    TotalBet = 0,
                    PaidOut = amount,
                    MaxBet = 0,
                    MaxWin = amount,
                });
                return Task.CompletedTask;
            }
        }
        else if (txData.Type == "animalrace")
        {
            if (txData.Extra == "refund")
            {
                userStats.Add(new()
                {
                    UserId = userId,
                    Game = type,
                    WinCount = 0,
                    LoseCount = -1,
                    TotalBet = -amount,
                    PaidOut = 0,
                    MaxBet = 0,
                    MaxWin = 0,
                });

                return Task.CompletedTask;
            }
        }

        userStats.Add(new UserBetStats()
        {
            UserId = userId,
            Game = type,
            WinCount = 1,
            LoseCount = -1,
            TotalBet = 0,
            PaidOut = amount,
            MaxBet = 0,
            MaxWin = amount,
        });

        return Task.CompletedTask;
    }

    public Task TrackRemove(ulong userId, long amount, TxData? txData)
    {
        if (txData is null)
            return Task.CompletedTask;

        if (_gamblingTypes.Contains(txData.Type))
        {
            globalStats.AddOrUpdate(txData.Type,
                _ => (amount, 0),
                (_, old) => (old.Bet + amount, old.PaidOut));
        }

        var mType = GetGameType(txData.Type);

        if (mType is not { } type)
            return Task.CompletedTask;

        userStats.Add(new UserBetStats()
        {
            UserId = userId,
            Game = type,
            WinCount = 0,
            LoseCount = 1,
            TotalBet = amount,
            PaidOut = 0,
            MaxBet = amount,
            MaxWin = 0
        });

        return Task.CompletedTask;
    }

    private static GamblingGame? GetGameType(string game)
        => game switch
        {
            "lula" => GamblingGame.Lula,
            "betroll" => GamblingGame.Betroll,
            "betflip" => GamblingGame.Betflip,
            "blackjack" => GamblingGame.Blackjack,
            "betdraw" => GamblingGame.Betdraw,
            "slot" => GamblingGame.Slots,
            "animalrace" => GamblingGame.Race,
            _ => null
        };

    public async Task<IReadOnlyCollection<GamblingStats>> GetAllAsync()
    {
        await using var ctx = _db.GetDbContext();
        return await ctx.Set<GamblingStats>()
                        .ToListAsyncEF();
    }

    public async Task<List<UserBetStats>> GetUserStatsAsync(ulong userId, GamblingGame? game = null)
    {
        await using var ctx = _db.GetDbContext();


        if (game is null)
            return await ctx
                         .GetTable<UserBetStats>()
                         .Where(x => x.UserId == userId)
                         .ToListAsync();

        return await ctx
                     .GetTable<UserBetStats>()
                     .Where(x => x.UserId == userId && x.Game == game)
                     .ToListAsync();
    }

    public decimal GetHouseEdge(GamblingGame game)
        => game switch
        {
            GamblingGame.Betflip => 0.025m,
            GamblingGame.Betroll => 0.04m,
            GamblingGame.Betdraw => 0.04m,
            GamblingGame.Slots => 0.034m,
            GamblingGame.Blackjack => 0.02m,
            GamblingGame.Lula => 0.025m,
            GamblingGame.Race => 0.06m,
            _ => 0
        };

  
}

public sealed class UserBetStats
{
    public int Id { get; set; }
    public ulong UserId { get; set; }
    public GamblingGame Game { get; set; }
    public long WinCount { get; set; }
    public long LoseCount { get; set; }
    public decimal TotalBet { get; set; }
    public decimal PaidOut { get; set; }
    public long MaxWin { get; set; }
    public long MaxBet { get; set; }
}

public enum GamblingGame
{
    Betflip = 0,
    Bf = 0,
    Betroll = 1,
    Br = 1,
    Betdraw = 2,
    Bd = 2,
    Slots = 3,
    Slot = 3,
    Blackjack = 4,
    Bj = 4,
    Lula = 5,
    Race = 6,
    AnimalRace = 6
}

public sealed class Rakeback
{
    public ulong UserId { get; set; }
    public decimal Amount { get; set; }
}