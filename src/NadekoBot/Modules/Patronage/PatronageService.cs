using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Patronage;

/// <inheritdoc cref="IPatronageService"/>
public sealed class PatronageService
    : IPatronageService,
        IReadyExecutor,
        INService
{
    public event Func<Patron, Task> OnNewPatronPayment = static delegate { return Task.CompletedTask; };
    public event Func<Patron, Patron, Task> OnPatronUpdated = static delegate { return Task.CompletedTask; };
    public event Func<Patron, Task> OnPatronRefunded = static delegate { return Task.CompletedTask; };

    // this has to run right before the command
    public int Priority
        => int.MinValue;

    private readonly PatronageConfig _pConf;
    private readonly DbService _db;
    private readonly DiscordSocketClient _client;
    private readonly ISubscriptionHandler _subsHandler;

    private readonly IBotCache _cache;
    private readonly IBotCredsProvider _creds;
    private readonly IMessageSenderService _sender;

    public PatronageService(
        PatronageConfig pConf,
        DbService db,
        DiscordSocketClient client,
        ISubscriptionHandler subsHandler,
        IBotCache cache,
        IBotCredsProvider creds,
        IMessageSenderService sender)
    {
        _pConf = pConf;
        _db = db;
        _client = client;
        _subsHandler = subsHandler;
        _sender = sender;
        _cache = cache;
        _creds = creds;
    }

    public Task OnReadyAsync()
    {
        if (_client.ShardId != 0)
            return Task.CompletedTask;

        return Task.WhenAll(LoadSubscribersLoopAsync());
    }

    private async Task LoadSubscribersLoopAsync()
    {
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                if (!_pConf.Data.IsEnabled)
                    continue;

                await foreach (var batch in _subsHandler.GetPatronsAsync())
                {
                    await ProcesssPatronsAsync(batch);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error processing patrons");
            }
        }
    }

    private async Task ProcesssPatronsAsync(IReadOnlyCollection<ISubscriberData> subscribersEnum)
    {
        // process only users who have discord accounts connected
        var subscribers = subscribersEnum.Where(x => x.UserId != 0).ToArray();

        if (subscribers.Length == 0)
            return;

        var todayDate = DateTime.UtcNow.Date;
        await using var ctx = _db.GetDbContext();

        // handle paid users
        foreach (var subscriber in subscribers.Where(x => x.ChargeStatus == SubscriptionChargeStatus.Paid))
        {
            if (subscriber.LastCharge is null)
                continue;

            var lastChargeUtc = subscriber.LastCharge.Value.ToUniversalTime();
            var dateInOneMonth = lastChargeUtc.Date.AddMonths(1);
            try
            {
                var dbPatron = await ctx.GetTable<PatronUser>()
                                        .FirstOrDefaultAsync(x
                                            => x.UniquePlatformUserId == subscriber.UniquePlatformUserId);

                if (dbPatron is null)
                {
                    // if the user is not in the database alrady
                    dbPatron = await ctx.GetTable<PatronUser>()
                                        .InsertWithOutputAsync(() => new()
                                        {
                                            UniquePlatformUserId = subscriber.UniquePlatformUserId,
                                            UserId = subscriber.UserId,
                                            AmountCents = subscriber.Cents,
                                            LastCharge = lastChargeUtc,
                                            ValidThru = dateInOneMonth,
                                        });

                    // await tran.CommitAsync();

                    var newPatron = PatronUserToPatron(dbPatron);
                    _ = SendWelcomeMessage(newPatron);
                    await OnNewPatronPayment(newPatron);
                }
                else
                {
                    if (dbPatron.LastCharge.Month < lastChargeUtc.Month
                        || dbPatron.LastCharge.Year < lastChargeUtc.Year)
                    {
                        // user is charged again for this month
                        // if his sub would end in teh future, extend it by one month.
                        // if it's not, just add 1 month to the last charge date
                        await ctx.GetTable<PatronUser>()
                                 .Where(x => x.UniquePlatformUserId
                                             == subscriber.UniquePlatformUserId)
                                 .UpdateAsync(old => new()
                                 {
                                     UserId = subscriber.UserId,
                                     AmountCents = subscriber.Cents,
                                     LastCharge = lastChargeUtc,
                                     ValidThru = old.ValidThru >= todayDate
                                         // ? Sql.DateAdd(Sql.DateParts.Month, 1, old.ValidThru).Value
                                         ? old.ValidThru.AddMonths(1)
                                         : dateInOneMonth,
                                 });


                        dbPatron.UserId = subscriber.UserId;
                        dbPatron.AmountCents = subscriber.Cents;
                        dbPatron.LastCharge = lastChargeUtc;
                        dbPatron.ValidThru = dbPatron.ValidThru >= todayDate
                            ? dbPatron.ValidThru.AddMonths(1)
                            : dateInOneMonth;

                        await OnNewPatronPayment(PatronUserToPatron(dbPatron));
                    }
                    else if (dbPatron.AmountCents != subscriber.Cents // if user changed the amount 
                             || dbPatron.UserId != subscriber.UserId) // if user updated user id)
                    {
                        var cents = subscriber.Cents;
                        // the user updated the pledge or changed the connected discord account
                        await ctx.GetTable<PatronUser>()
                                 .Where(x => x.UniquePlatformUserId == subscriber.UniquePlatformUserId)
                                 .UpdateAsync(old => new()
                                 {
                                     UserId = subscriber.UserId,
                                     AmountCents = cents,
                                     LastCharge = lastChargeUtc,
                                     ValidThru = old.ValidThru,
                                 });

                        var newPatron = dbPatron.Clone();
                        newPatron.AmountCents = cents;
                        newPatron.UserId = subscriber.UserId;

                        // idk what's going on but UpdateWithOutputAsync doesn't work properly here
                        // nor does firstordefault after update. I'm not seeing something obvious
                        await OnPatronUpdated(
                            PatronUserToPatron(dbPatron),
                            PatronUserToPatron(newPatron));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex,
                    "Unexpected error occured while processing rewards for patron {UserId}",
                    subscriber.UserId);
            }
        }

        var expiredDate = DateTime.MinValue;
        foreach (var patron in subscribers.Where(x => x.ChargeStatus == SubscriptionChargeStatus.Refunded))
        {
            // if the subscription is refunded, Disable user's valid thru 
            var changedCount = await ctx.GetTable<PatronUser>()
                                        .Where(x => x.UniquePlatformUserId == patron.UniquePlatformUserId
                                                    && x.ValidThru != expiredDate)
                                        .UpdateAsync(old => new()
                                        {
                                            ValidThru = expiredDate
                                        });

            if (changedCount == 0)
                continue;

            var updated = await ctx.GetTable<PatronUser>()
                                   .Where(x => x.UniquePlatformUserId == patron.UniquePlatformUserId)
                                   .FirstAsync();

            await OnPatronRefunded(PatronUserToPatron(updated));
        }
    }

    public async Task<Patron?> GetPatronAsync(ulong userId)
    {
        await using var ctx = _db.GetDbContext();

        // this can potentially return multiple users if the user
        // is subscribed on multiple platforms
        // or if there are multiple users on the same platform who connected the same discord account?!
        var users = await ctx.GetTable<PatronUser>()
                             .Where(x => x.UserId == userId)
                             .ToListAsync();

        // first find all active subscriptions
        // and return the one with the highest amount
        var maxActive = users.Where(x => !x.ValidThru.IsBeforeToday()).MaxBy(x => x.AmountCents);
        if (maxActive is not null)
            return PatronUserToPatron(maxActive);

        // if there are no active subs, return the one with the highest amount

        var max = users.MaxBy(x => x.AmountCents);
        if (max is null)
            return default; // no patron with that name

        return PatronUserToPatron(max);
    }

    public async Task<bool> LimitHitAsync(LimitedFeatureName key, ulong userId, int amount = 1)
    {
        if (_creds.GetCreds().IsOwner(userId))
            return true;

        if (!_pConf.Data.IsEnabled)
            return true;

        var userLimit = await GetUserLimit(key, userId);

        if (userLimit.Quota == 0)
            return false;

        if (userLimit.Quota == -1)
            return true;

        return await TryAddLimit(key, userLimit, userId, amount);
    }

    public async Task<bool> LimitForceHit(LimitedFeatureName key, ulong userId, int amount)
    {
        if (_creds.GetCreds().IsOwner(userId))
            return true;

        if (!_pConf.Data.IsEnabled)
            return true;

        var userLimit = await GetUserLimit(key, userId);

        var cacheKey = CreateKey(key, userId);
        await _cache.GetOrAddAsync(cacheKey, () => Task.FromResult(0), GetExpiry(userLimit));

        return await TryAddLimit(key, userLimit, userId, amount);
    }

    private async Task<bool> TryAddLimit(
        LimitedFeatureName key,
        QuotaLimit userLimit,
        ulong userId,
        int amount)
    {
        var cacheKey = CreateKey(key, userId);
        var cur = await _cache.GetOrAddAsync(cacheKey, () => Task.FromResult(0), GetExpiry(userLimit));

        if (cur + amount < userLimit.Quota)
        {
            await _cache.AddAsync(cacheKey, cur + amount);
            return true;
        }

        return false;
    }

    private TimeSpan? GetExpiry(QuotaLimit userLimit)
    {
        var now = DateTime.UtcNow;
        switch (userLimit.QuotaPeriod)
        {
            case QuotaPer.PerHour:
                return TimeSpan.FromMinutes(60 - now.Minute);
            case QuotaPer.PerDay:
                return TimeSpan.FromMinutes((24 * 60) - ((now.Hour * 60) + now.Minute));
            case QuotaPer.PerMonth:
                var firstOfNextMonth = now.FirstOfNextMonth();
                return firstOfNextMonth - now;
            default:
                return null;
        }
    }

    private TypedKey<int> CreateKey(LimitedFeatureName key, ulong userId)
        => new($"limited_feature:{key}:{userId}");

    private readonly QuotaLimit _emptyQuota = new QuotaLimit()
    {
        Quota = 0,
        QuotaPeriod = QuotaPer.PerDay,
    };

    private readonly QuotaLimit _infiniteQuota = new QuotaLimit()
    {
        Quota = -1,
        QuotaPeriod = QuotaPer.PerDay,
    };

    public async Task<QuotaLimit> GetUserLimit(LimitedFeatureName name, ulong userId)
    {
        if (!_pConf.Data.IsEnabled)
            return _infiniteQuota;
        
        var maybePatron = await GetPatronAsync(userId);

        if (maybePatron is not { } patron)
            return _emptyQuota;

        if (patron.ValidThru < DateTime.UtcNow)
            return _emptyQuota;

        foreach (var (key, value) in _pConf.Data.Limits)
        {
            if (patron.Amount >= key)
            {
                if (value.TryGetValue(name, out var quotaLimit))
                {
                    return quotaLimit;
                }

                break;
            }
        }

        return _emptyQuota;
    }

    public async Task<Dictionary<LimitedFeatureName, (int, QuotaLimit)>> LimitStats(ulong userId)
    {
        var dict = new Dictionary<LimitedFeatureName, (int, QuotaLimit)>();
        foreach (var featureName in Enum.GetValues<LimitedFeatureName>())
        {
            var cacheKey = CreateKey(featureName, userId);
            var userLimit = await GetUserLimit(featureName, userId);
            var cur = await _cache.GetOrAddAsync(cacheKey, () => Task.FromResult(0), GetExpiry(userLimit));

            dict[featureName] = (cur, userLimit);
        }

        return dict;
    }


    private Patron PatronUserToPatron(PatronUser user)
        => new Patron()
        {
            UniquePlatformUserId = user.UniquePlatformUserId,
            UserId = user.UserId,
            Amount = user.AmountCents,
            Tier = CalculateTier(user),
            PaidAt = user.LastCharge,
            ValidThru = user.ValidThru,
        };

    private PatronTier CalculateTier(PatronUser user)
    {
        if (user.ValidThru.IsBeforeToday())
            return PatronTier.None;

        return user.AmountCents switch
        {
            >= 10_000 => PatronTier.C,
            >= 5000 => PatronTier.L,
            >= 2000 => PatronTier.XX,
            >= 1000 => PatronTier.X,
            >= 500 => PatronTier.V,
            >= 100 => PatronTier.I,
            _ => PatronTier.None
        };
    }

    public int PercentBonus(Patron? maybePatron)
        => maybePatron is { } user && user.ValidThru > DateTime.UtcNow
            ? PercentBonus(user.Amount)
            : 0;

    public int PercentBonus(long amount)
        => amount switch
        {
            >= 10_000 => 100,
            >= 5000 => 50,
            >= 2000 => 30,
            >= 1000 => 20,
            >= 500 => 10,
            _ => 0
        };

    private async Task SendWelcomeMessage(Patron patron)
    {
        try
        {
            var user = (IUser)_client.GetUser(patron.UserId) ?? await _client.Rest.GetUserAsync(patron.UserId);
            if (user is null)
                return;

            var eb = _sender.CreateEmbed()
                            .WithOkColor()
                            .WithTitle("❤️ Thank you for supporting NadekoBot! ❤️")
                            .WithDescription(
                                "Your donation has been processed and you will receive the rewards shortly.\n"
                                + "You can visit <https://www.patreon.com/join/nadekobot> to see rewards for your tier. 🎉")
                            .AddField("Tier", Format.Bold(patron.Tier.ToString()), true)
                            .AddField("Pledge", $"**{patron.Amount / 100.0f:N1}$**", true)
                            .AddField("Expires",
                                patron.ValidThru.AddDays(1).ToShortAndRelativeTimestampTag(),
                                true)
                            .AddField("Instructions",
                                """
                                *- Within the next **1-2 minutes** you will have all of the benefits of the Tier you've subscribed to.*
                                *- You can check your benefits on <https://www.patreon.com/join/nadekobot>*
                                *- You can use the `.patron` command in this chat to check your current quota usage for the Patron-only commands*
                                *- **ALL** of the servers that you **own** will enjoy your Patron benefits.*
                                *- You can use any of the commands available in your tier on any server (assuming you have sufficient permissions to run those commands)*
                                *- Any user in any of your servers can use Patron-only commands, but they will spend **your quota**, which is why it's recommended to use Nadeko's command cooldown system (.h .cmdcd) or permission system to limit the command usage for your server members.*
                                *- Permission guide can be found here if you're not familiar with it: <https://nadekobot.readthedocs.io/en/latest/permissions-system/>*
                                """,
                                inline: false)
                            .WithFooter($"platform id: {patron.UniquePlatformUserId}");

            await _sender.Response(user).Embed(eb).SendAsync();
        }
        catch
        {
            Log.Warning("Unable to send a \"Welcome\" message to the patron {UserId}", patron.UserId);
        }
    }

    public async Task<(int Success, int Failed)> SendMessageToPatronsAsync(PatronTier tierAndHigher, string message)
    {
        await using var ctx = _db.GetDbContext();

        var patrons = await ctx.GetTable<PatronUser>()
                               .Where(x => x.ValidThru > DateTime.UtcNow)
                               .ToArrayAsync();

        var text = SmartText.CreateFrom(message);

        var succ = 0;
        var fail = 0;
        foreach (var patron in patrons)
        {
            try
            {
                var user = await _client.GetUserAsync(patron.UserId);
                await _sender.Response(user).Text(text).SendAsync();
                ++succ;
            }
            catch
            {
                ++fail;
            }

            await Task.Delay(1000);
        }

        return (succ, fail);
    }

    public PatronConfigData GetConfig()
        => _pConf.Data;
}