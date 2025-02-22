#nullable disable
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Gambling.Common;
using NadekoBot.Modules.Gambling.Common.Connect4;

namespace NadekoBot.Modules.Gambling.Services;

public class GamblingService : INService, IReadyExecutor
{
    public ConcurrentDictionary<(ulong, ulong), RollDuelGame> Duels { get; } = new();
    public ConcurrentDictionary<ulong, Connect4Game> Connect4Games { get; } = new();
    private readonly DbService _db;
    private readonly DiscordSocketClient _client;
    private readonly IBotCache _cache;
    private readonly GamblingConfigService _gss;
    private readonly NadekoRandom _rng;

    private static readonly TypedKey<long> _curDecayKey = new("currency:last_decay");

    public GamblingService(
        DbService db,
        DiscordSocketClient client,
        IBotCache cache,
        GamblingConfigService gss)
    {
        _db = db;
        _client = client;
        _cache = cache;
        _gss = gss;
        _rng = new NadekoRandom();
    }

    public Task OnReadyAsync()
        => Task.WhenAll(CurrencyDecayLoopAsync(), TransactionClearLoopAsync());


    public string GeneratePassword()
    {
        var num = _rng.Next((int)Math.Pow(31, 2), (int)Math.Pow(32, 3));
        return new kwum(num).ToString();
    }

    private async Task TransactionClearLoopAsync()
    {
        if (_client.ShardId != 0)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                var lifetime = _gss.Data.Currency.TransactionsLifetime;
                if (lifetime <= 0)
                    continue;

                var now = DateTime.UtcNow;
                var days = TimeSpan.FromDays(lifetime);
                await using var uow = _db.GetDbContext();
                await uow.Set<CurrencyTransaction>()
                         .DeleteAsync(ct => ct.DateAdded == null || now - ct.DateAdded < days);
            }
            catch (Exception ex)
            {
                Log.Warning(ex,
                    "An unexpected error occurred in transactions cleanup loop: {ErrorMessage}",
                    ex.Message);
            }
        }
    }

    private async Task CurrencyDecayLoopAsync()
    {
        if (_client.ShardId != 0)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                var config = _gss.Data;
                var maxDecay = config.Decay.MaxDecay;
                if (config.Decay.Percent is <= 0 or > 1 || maxDecay < 0)
                    continue;

                var now = DateTime.UtcNow;

                await using var uow = _db.GetDbContext();
                var result = await _cache.GetAsync(_curDecayKey);

                if (result.TryPickT0(out var bin, out _)
                    && (now - DateTime.FromBinary(bin) < TimeSpan.FromHours(config.Decay.HourInterval)))
                {
                    continue;
                }

                Log.Information("""
                                --- Decaying users' currency ---
                                | decay: {ConfigDecayPercent}% 
                                | max: {MaxDecay} 
                                | threshold: {DecayMinTreshold}
                                """,
                    config.Decay.Percent * 100,
                    maxDecay,
                    config.Decay.MinThreshold);

                if (maxDecay == 0)
                    maxDecay = int.MaxValue;

                var decay = (double)config.Decay.Percent;
                await uow.Set<DiscordUser>()
                         .Where(x => x.CurrencyAmount > config.Decay.MinThreshold && x.UserId != _client.CurrentUser.Id)
                         .UpdateAsync(old => new()
                         {
                             CurrencyAmount =
                                 maxDecay > Sql.Round((old.CurrencyAmount * decay) - 0.5)
                                     ? (long)(old.CurrencyAmount - Sql.Round((old.CurrencyAmount * decay) - 0.5))
                                     : old.CurrencyAmount - maxDecay
                         });

                await uow.SaveChangesAsync();

                await _cache.AddAsync(_curDecayKey, now.ToBinary());
            }
            catch (Exception ex)
            {
                Log.Warning(ex,
                    "An unexpected error occurred in currency decay loop: {ErrorMessage}",
                    ex.Message);
            }
        }
    }

    private static readonly TypedKey<EconomyResult> _ecoKey = new("nadeko:economy");

    private static readonly SemaphoreSlim _timelyLock = new(1, 1);

    private static TypedKey<Dictionary<ulong, long>> _timelyKey
        = new("timely:claims");


    public async Task<TimeSpan?> ClaimTimelyAsync(ulong userId, int period)
    {
        if (period == 0)
            return null;

        await _timelyLock.WaitAsync();
        try
        {
            // get the dictionary from the cache or get a new one
            var dict = (await _cache.GetOrAddAsync(_timelyKey,
                () => Task.FromResult(new Dictionary<ulong, long>())))!;

            var now = DateTime.UtcNow;
            var nowB = now.ToBinary();

            // try to get users last claim
            if (!dict.TryGetValue(userId, out var lastB))
                lastB = dict[userId] = now.ToBinary();

            var diff = now - DateTime.FromBinary(lastB);

            // if its now, or too long ago => success
            if (lastB == nowB || diff > period.Hours())
            {
                // update the cache
                dict[userId] = nowB;
                await _cache.AddAsync(_timelyKey, dict);

                return null;
            }
            else
            {
                // otherwise return the remaining time
                return period.Hours() - diff;
            }
        }
        finally
        {
            _timelyLock.Release();
        }
    }

    public bool UserHasTimelyReminder(ulong userId)
    {
        var db = _db.GetDbContext();
        return db.GetTable<Reminder>()
                 .Any(x => x.UserId == userId
                           && x.Type == ReminderType.Timely);
    }

    public async Task RemoveAllTimelyClaimsAsync()
        => await _cache.RemoveAsync(_timelyKey);
}