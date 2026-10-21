#nullable disable
using System.Globalization;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Gambling.Common;
using NadekoBot.Modules.Gambling.Common.Connect4;
using NadekoBot.Modules.Games.Quests;
using NadekoBot.Modules.Patronage;

namespace NadekoBot.Modules.Gambling.Services;

public class GamblingService : INService, IReadyExecutor
{
    public ConcurrentDictionary<(ulong, ulong), RollDuelGame> Duels { get; } = new();
    public ConcurrentDictionary<ulong, Connect4Game> Connect4Games { get; } = new();
    private readonly DbService _db;
    private readonly DiscordSocketClient _client;
    private readonly IBotCache _cache;
    private readonly GamblingConfigService _gcs;
    private readonly IPatronageService _ps;
    private readonly QuestService _quests;
    private readonly NadekoRandom _rng;
    private readonly TimelyClaimStore _timely;

    private static readonly TypedKey<long> _curDecayKey = new("currency:last_decay");

    public GamblingService(
        DbService db,
        DiscordSocketClient client,
        IBotCache cache,
        GamblingConfigService gcs,
        IPatronageService ps,
        QuestService quests)
    {
        _db = db;
        _client = client;
        _cache = cache;
        _gcs = gcs;
        _ps = ps;
        _quests = quests;
        _rng = new NadekoRandom();
        _timely = new(cache, TimeProvider.System);
    }

    public async Task OnReadyAsync()
    {
        if (_client.ShardId == 0)
        {
            try
            {
                await _cache.RemoveAsync(_oldTimelyKey);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Unable to delete the old timely claim list");
            }
        }

        await Task.WhenAll(CurrencyDecayLoopAsync(), TransactionClearLoopAsync());
    }


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
                var lifetime = _gcs.Data.Currency.TransactionsLifetime;
                if (lifetime <= 0)
                    continue;

                // The cutoff is calculated here, because SQLite cannot subtract two dates
                // and silently matches every row when it tries.
                var cutoff = DateTime.UtcNow - TimeSpan.FromDays(lifetime);
                await using var uow = _db.GetDbContext();
                await uow.GetTable<CurrencyTransaction>()
                    .Where(ct => ct.DateAdded == null || ct.DateAdded < cutoff)
                    .DeleteAsync();
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
                var config = _gcs.Data;
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

    // the claim list of earlier versions, which shard 0 deletes once on start
    private static readonly TypedKey<Dictionary<ulong, long>> _oldTimelyKey = new("timely:claims");

    public Task<TimeSpan?> ClaimTimelyAsync(ulong userId, int period)
        => _timely.TryClaimAsync(userId, period);

    public async Task<bool> UserHasTimelyReminderAsync(ulong userId)
    {
        await using var ctx = _db.GetDbContext();
        return await ctx.GetTable<Reminder>()
            .AnyAsyncLinqToDB(x => x.UserId == userId
                                   && x.Type == ReminderType.Timely);
    }

    public Task RemoveAllTimelyClaimsAsync()
        => _timely.ResetAllAsync();

    private string N(long amount)
        => CurrencyHelper.N(amount, CultureInfo.InvariantCulture, _gcs.Data.Currency.Sign);

    public async Task<(long val, string msg)> GetAmountAndMessage(ulong userId, long baseAmount)
    {
        var totalAmount = baseAmount;
        var gcsData = _gcs.Data;
        var boostGuilds = gcsData.BoostBonus.GuildIds ?? [];
        var guildUsers = await boostGuilds
            .Select(async gid =>
            {
                try
                {
                    var guild = _client.GetGuild(gid) as IGuild ?? await _client.Rest.GetGuildAsync(gid, false);
                    var user = await guild.GetUserAsync(userId) ?? await _client.Rest.GetGuildUserAsync(gid, userId);
                    return (guild, user);
                }
                catch
                {
                    return default;
                }
            })
            .WhenAll();

        var userInfo = guildUsers.FirstOrDefault(x => x.user?.PremiumSince is not null);
        var booster = userInfo != default;

        if (booster)
            totalAmount += gcsData.BoostBonus.BaseTimelyBonus;

        var hasCompletedDailies = await _quests.UserCompletedDailies(userId);

        if (hasCompletedDailies)
            totalAmount = (long)(1.5 * totalAmount);

        var patron = await _ps.GetPatronAsync(userId);
        var percentBonus = (_ps.PercentBonus(patron) / 100f);

        totalAmount += (long)(totalAmount * percentBonus);

        var msg = $"**{N(baseAmount)}** base reward\n\n";
        if (boostGuilds.Count > 0)
        {
            if (booster)
                msg += $"✅ *+{N(gcsData.BoostBonus.BaseTimelyBonus)} bonus for boosting {userInfo.guild}!*\n";
            else
                msg += $"❌ *+0 bonus for boosting {userInfo.guild}*\n";
        }

        if (_ps.IsEnabled)
        {
            if (percentBonus > float.Epsilon)
                msg +=
                    $"✅ *+{percentBonus:P0} bonus for the [Patreon](https://patreon.com/nadekobot) pledge! <:hart:746995901758832712>*\n";
            else
                msg += $"❌ *+0 bonus for the [Patreon](https://patreon.com/nadekobot) pledge*\n";
        }

        if (hasCompletedDailies)
        {
            msg += $"✅ *+50% bonus for completing daily quests*\n";
        }
        else
        {
            msg += $"❌ *+0 bonus for completing daily quests*\n";
        }


        return (totalAmount, msg);
    }
}