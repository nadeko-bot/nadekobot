namespace NadekoBot.Services;

public enum YoutubeOutcome
{
    Ok,
    BotCheck,
    RateLimited,
    Forbidden,
    ProxyError,
    NoJsRuntime,
    OtherError,
}

public readonly record struct YoutubeCounts(
    long Ok,
    long BotCheck,
    long RateLimited,
    long Forbidden,
    long ProxyErrors,
    long NoJsRuntime,
    long OtherErrors,
    long ProxyBytes);

// How YouTube answers this bot, in 10-minute buckets for the last 24 hours. Memory only: a restart starts at zero.
public sealed class YoutubeStats(TimeProvider time)
{
    public const int BUCKET_MINUTES = 10;
    public const int BUCKETS = 24 * 60 / BUCKET_MINUTES;
    public const int HOUR_BUCKETS = 60 / BUCKET_MINUTES;

    private const int BYTES = 7;
    private const int FIELDS = 8;

    public static YoutubeStats Shared { get; } = new(TimeProvider.System);

    private readonly Lock _lock = new();
    private readonly long[] _starts = new long[BUCKETS];
    private readonly long[,] _counts = new long[BUCKETS, FIELDS];

    public void Record(YoutubeOutcome outcome)
        => AddInternal((int)outcome, 1);

    public void AddProxyBytes(long bytes)
    {
        if (bytes > 0)
            AddInternal(BYTES, bytes);
    }

    // Counts one run of yt-dlp. A run which printed nothing and named no error (no search results) is not counted.
    public void RecordYtdl(string? stderr, bool gotOutput)
    {
        var (outcome, noJsRuntime) = Classify(stderr, gotOutput);
        if (noJsRuntime)
            Record(YoutubeOutcome.NoJsRuntime);

        if (outcome is { } o)
            Record(o);
    }

    // Proxy failures are counted by PublicOnlyProxy, which sees every program, so yt-dlp's copy of them is not.
    public static (YoutubeOutcome? Outcome, bool NoJsRuntime) Classify(string? stderr, bool gotOutput)
    {
        var err = stderr ?? string.Empty;
        var noJs = err.Contains("No supported JavaScript runtime", StringComparison.OrdinalIgnoreCase);
        if (gotOutput)
            return (YoutubeOutcome.Ok, noJs);

        if (err.Contains("Sign in to confirm", StringComparison.OrdinalIgnoreCase)
            || err.Contains("not a bot", StringComparison.OrdinalIgnoreCase))
            return (YoutubeOutcome.BotCheck, noJs);

        if (err.Contains("HTTP Error 429", StringComparison.OrdinalIgnoreCase)
            || err.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase)
            || err.Contains("rate-limited", StringComparison.OrdinalIgnoreCase)
            || err.Contains("rate limited", StringComparison.OrdinalIgnoreCase))
            return (YoutubeOutcome.RateLimited, noJs);

        if (err.Contains("Unable to connect to proxy", StringComparison.OrdinalIgnoreCase)
            || err.Contains("Tunnel connection failed", StringComparison.OrdinalIgnoreCase)
            || err.Contains("ProxyError", StringComparison.Ordinal))
            return (null, noJs);

        if (err.Contains("HTTP Error 403", StringComparison.OrdinalIgnoreCase))
            return (YoutubeOutcome.Forbidden, noJs);

        if (err.Contains("ERROR", StringComparison.Ordinal))
            return (YoutubeOutcome.OtherError, noJs);

        return (null, noJs);
    }

    public YoutubeCounts LastHour()
        => SumInternal(HOUR_BUCKETS);

    public YoutubeCounts LastDay()
        => SumInternal(BUCKETS);

    private static long BucketStart(DateTimeOffset now)
        => now.ToUnixTimeSeconds() / (BUCKET_MINUTES * 60);

    private void AddInternal(int field, long amount)
    {
        var start = BucketStart(time.GetUtcNow());
        var i = (int)(start % BUCKETS);
        lock (_lock)
        {
            if (_starts[i] != start)
            {
                _starts[i] = start;
                for (var f = 0; f < FIELDS; f++)
                    _counts[i, f] = 0;
            }

            _counts[i, field] += amount;
        }
    }

    private YoutubeCounts SumInternal(int buckets)
    {
        var now = BucketStart(time.GetUtcNow());
        var sums = new long[FIELDS];
        lock (_lock)
        {
            for (var i = 0; i < BUCKETS; i++)
            {
                if (_starts[i] > now || _starts[i] <= now - buckets)
                    continue;

                for (var f = 0; f < FIELDS; f++)
                    sums[f] += _counts[i, f];
            }
        }

        return new(sums[0], sums[1], sums[2], sums[3], sums[4], sums[5], sums[6], sums[7]);
    }
}
