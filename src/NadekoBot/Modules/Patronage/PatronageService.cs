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

    private Func<string, ulong, TypedKey<int>> Limitkey
        => (name, userId) => new($"patron_limit:{userId}:{name}");

    public async Task<bool> LimitHitAsync(string name, ulong userId, int defaultMax)
    {
        var data = _pConf.Data;
        if (!data.IsEnabled)
            return true;

        var limit = await GetUserLimit(name, userId, defaultMax);

        if (limit == -1)
            return true;
        
        var timeUntilTomorrow = (DateTime.UtcNow.Date.AddDays(1) - DateTime.UtcNow);
        var soFar = await _cache.GetOrAddAsync(Limitkey(name, userId),
            () => Task.FromResult(0),
            expiry: timeUntilTomorrow);

        if (soFar >= limit)
            return false;

        await _cache.AddAsync(Limitkey(name, userId), soFar + 1, timeUntilTomorrow, overwrite: true);
        return true;
    }

    public async Task<int> GetUserLimit(string name, ulong userId, int defaultMax)
    {
        var data = _pConf.Data;
        if (!data.IsEnabled || _creds.GetCreds().OwnerIds.Contains(userId))
            return defaultMax;

        var mPatron = await GetPatronAsync(userId);

        if (mPatron is not { } patron || !patron.IsActive)
        {
            if (data.Quotas.TryGetValue(PatronTier.I, out var limits)
                && limits.TryGetValue(name, out var limit))
                return limit;

            return 0;
        }

        if (data.Quotas.TryGetValue(patron.Tier, out var plimits)
            && plimits.TryGetValue(name, out var plimit))
            return plimit;

        return 0;
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
            >= 5_000 => PatronTier.L,
            >= 2_000 => PatronTier.XX,
            >= 1_000 => PatronTier.X,
            >= 500 => PatronTier.V,
            >= 100 => PatronTier.I,
            _ => 0,
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
            >= 5_000 => 50,
            >= 2_000 => 25,
            >= 1_000 => 10,
            >= 500 => 5,
            _ => 0,
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
                    *- You can use the `.patron` command in this chat to check your current quota usage for the Patron-only commands*
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