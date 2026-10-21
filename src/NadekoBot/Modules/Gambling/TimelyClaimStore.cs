namespace NadekoBot.Modules.Gambling.Services;

public sealed class TimelyClaimStore(IBotCache cache, TimeProvider time)
{
    private const int MAX_ATTEMPTS = 3;

    private static readonly TimeSpan _retryAfter = TimeSpan.FromSeconds(5);

    private static readonly TypedKey<long> _epochKey = new("timely:epoch");

    private static TypedKey<long> ClaimKey(long epoch, ulong userId)
        => new($"timely:claim:{epoch}:{userId}");

    // returns null when the claim succeeds, otherwise the time until the next claim
    public async Task<TimeSpan?> TryClaimAsync(ulong userId, int periodHours)
    {
        if (periodHours <= 0)
            return null;

        var period = TimeSpan.FromHours(periodHours);
        var epoch = await cache.GetOrDefaultAsync(_epochKey);
        var key = ClaimKey(epoch, userId);

        for (var i = 0; i < MAX_ATTEMPTS; i++)
        {
            var now = time.GetUtcNow().UtcTicks;
            if (await cache.AddAsync(key, now, period, overwrite: false))
                return null;

            var claimedAt = await cache.GetAsync(key);
            if (!claimedAt.TryPickT0(out var claimedTicks, out _))
                continue;

            var remaining = period - TimeSpan.FromTicks(now - claimedTicks);
            if (remaining > TimeSpan.Zero)
                return remaining;

            // the claim is past its period but not yet evicted
            await cache.RemoveAsync(key);
        }

        Log.Warning("Timely claim of {UserId} could not be stored after {Attempts} attempts", userId, MAX_ATTEMPTS);
        return _retryAfter;
    }

    public async Task ResetAllAsync()
        => await cache.AddAsync(_epochKey, time.GetUtcNow().UtcTicks);
}
