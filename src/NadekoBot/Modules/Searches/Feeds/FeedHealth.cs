using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NadekoBot.Modules.Searches.Services;

public readonly record struct FeedFailure(bool StreakStarted, bool Remove, TimeSpan Streak);

// Poll schedule and failure streaks of the feeds on one shard. Only the feed loop uses it, so it has no locks.
public sealed class FeedHealth(TimeProvider time)
{
    public static readonly TimeSpan MinPollInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaxPollInterval = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan MaxFailureBackoff = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan MinHostPause = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan FirstHostPause = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MaxHostPause = TimeSpan.FromHours(1);
    public static readonly TimeSpan HostDownAfter = TimeSpan.FromHours(1);
    public static readonly TimeSpan WarnAfter = TimeSpan.FromHours(1);
    public static readonly TimeSpan RemoveAfter = TimeSpan.FromDays(3);
    public static readonly TimeSpan RemoveAfterHostDown = TimeSpan.FromDays(14);

    // Poll just after the cached copy expires, so the next request gets the new copy.
    private static readonly TimeSpan _freshnessSlack = TimeSpan.FromSeconds(2);

    // With one feed, a host outage and a dead feed look the same.
    private const int MIN_FEEDS_FOR_HOST_OUTAGE = 2;
    private const int MAX_BACKOFF_SHIFT = 16;

    private struct FeedState
    {
        public string Url;
        public string Host;
        public DateTime NextPollAt;
        public DateTime? FailingSince;
        public int Failures;
        public int Generation;
        public bool Warned;
    }

    private struct HostState
    {
        public DateTime CreatedAt;
        public DateTime? LastSuccessAt;
        public DateTime PausedUntil;
        public int PauseStep;
        public int FeedCount;
        public int Generation;
        public bool ReportedDown;
    }

    private readonly Dictionary<string, FeedState> _feeds = new();
    private readonly Dictionary<string, HostState> _hosts = new();
    private readonly List<string> _stale = new();
    private int _generation;

    private DateTime Now
        => time.GetUtcNow().UtcDateTime;

    // A tick touches every feed which has subs. Feeds and hosts which the tick does not touch are dropped in EndTick.
    public void BeginTick()
        => _generation++;

    public void Touch(string key, string url, DateTime? failingSince = null)
    {
        var now = Now;
        ref var feed = ref CollectionsMarshal.GetValueRefOrAddDefault(_feeds, key, out var exists);
        if (!exists)
        {
            feed.Url = url;
            feed.Host = GetHost(url);
            feed.NextPollAt = now;
            feed.FailingSince = failingSince;
        }

        feed.Generation = _generation;

        ref var host = ref CollectionsMarshal.GetValueRefOrAddDefault(_hosts, feed.Host, out var hostExists);
        if (!hostExists)
            host.CreatedAt = now;

        if (host.Generation != _generation)
        {
            host.Generation = _generation;
            host.FeedCount = 0;
        }

        host.FeedCount++;
    }

    public void EndTick()
    {
        foreach (var (key, feed) in _feeds)
        {
            if (feed.Generation != _generation)
                _stale.Add(key);
        }

        foreach (var key in _stale)
            _feeds.Remove(key);

        _stale.Clear();

        foreach (var (key, host) in _hosts)
        {
            if (host.Generation != _generation)
                _stale.Add(key);
        }

        foreach (var key in _stale)
            _hosts.Remove(key);

        _stale.Clear();
    }

    public void Forget(string key)
        => _feeds.Remove(key);

    public bool IsDue(string key)
    {
        if (!_feeds.TryGetValue(key, out var feed))
            return false;

        var now = Now;
        if (feed.NextPollAt > now)
            return false;

        return !_hosts.TryGetValue(feed.Host, out var host) || host.PausedUntil <= now;
    }

    // Returns true when the feed had a failure streak.
    public bool OnSuccess(string key, TimeSpan? freshness)
    {
        ref var feed = ref CollectionsMarshal.GetValueRefOrNullRef(_feeds, key);
        if (Unsafe.IsNullRef(ref feed))
            return false;

        var now = Now;
        var recovered = feed.FailingSince is not null;
        feed.FailingSince = null;
        feed.Failures = 0;
        feed.Warned = false;
        feed.NextPollAt = now + GetPollInterval(freshness);

        ref var host = ref CollectionsMarshal.GetValueRefOrNullRef(_hosts, feed.Host);
        if (Unsafe.IsNullRef(ref host))
            return recovered;

        host.LastSuccessAt = now;
        host.PauseStep = 0;
        if (host.ReportedDown)
        {
            host.ReportedDown = false;
            Log.Information("Feed host {Host} works again", feed.Host);
        }

        return recovered;
    }

    public FeedFailure OnFailure(string key, int status, TimeSpan? retryAfter)
    {
        ref var feed = ref CollectionsMarshal.GetValueRefOrNullRef(_feeds, key);
        if (Unsafe.IsNullRef(ref feed))
            return default;

        var now = Now;
        var started = feed.FailingSince is null;
        var failingSince = feed.FailingSince ??= now;
        feed.Failures++;
        feed.NextPollAt = now + GetFailureBackoff(feed.Failures);

        ref var host = ref CollectionsMarshal.GetValueRefOrAddDefault(_hosts, feed.Host, out var hostExists);
        if (!hostExists)
        {
            host.CreatedAt = now;
            host.Generation = _generation;
            host.FeedCount = 1;
        }

        if (status is (int)HttpStatusCode.TooManyRequests or (int)HttpStatusCode.Forbidden)
        {
            var pause = GetHostPause(retryAfter, host.PauseStep++);
            host.PausedUntil = now + pause;
            Log.Warning("Feed host {Host} answered {Status}. The bot pauses its feeds for {Pause}",
                feed.Host,
                status,
                pause);
        }

        var hostDown = IsHostDown(host.FeedCount, host.LastSuccessAt, now);
        if (hostDown && !host.ReportedDown && now - host.CreatedAt >= HostDownAfter)
        {
            host.ReportedDown = true;
            Log.Warning("Feed host {Host} is down: none of its {FeedCount} feeds worked for {Window}. Last status {Status}",
                feed.Host,
                host.FeedCount,
                HostDownAfter,
                status);
        }

        var streak = now - failingSince;
        if (!feed.Warned && !hostDown && streak >= WarnAfter)
        {
            feed.Warned = true;
            Log.Warning("Feed {FeedUrl} has failed for {Streak}. Last status {Status}", feed.Url, streak, status);
        }

        return new(started, ShouldRemove(streak, hostDown), streak);
    }

    public static bool IsHostDown(int feedCount, DateTime? lastSuccessAt, DateTime now)
        => feedCount >= MIN_FEEDS_FOR_HOST_OUTAGE
           && (lastSuccessAt is not { } last || now - last >= HostDownAfter);

    public static bool ShouldRemove(TimeSpan streak, bool hostDown)
        => streak >= RemoveAfterHostDown || (streak >= RemoveAfter && !hostDown);

    public static TimeSpan GetPollInterval(TimeSpan? freshness)
        => Clamp((freshness ?? TimeSpan.Zero) + _freshnessSlack, MinPollInterval, MaxPollInterval);

    public static TimeSpan GetFailureBackoff(int failures)
        => Doubled(MinPollInterval, failures - 1, MaxFailureBackoff);

    public static TimeSpan GetHostPause(TimeSpan? retryAfter, int step)
        => retryAfter is { } ra
            ? Clamp(ra, MinHostPause, MaxHostPause)
            : Doubled(FirstHostPause, step, MaxHostPause);

    public static TimeSpan? GetFreshness(HttpResponseMessage res, DateTimeOffset now)
    {
        var cc = res.Headers.CacheControl;
        if (cc is { NoStore: true } or { NoCache: true })
            return TimeSpan.Zero;

        if ((cc?.SharedMaxAge ?? cc?.MaxAge) is { } maxAge)
            return maxAge - (res.Headers.Age ?? TimeSpan.Zero);

        if (res.Content.Headers.Expires is { } expires)
            return expires - (res.Headers.Date ?? now);

        return null;
    }

    public static TimeSpan? GetRetryAfter(HttpResponseMessage res, DateTimeOffset now)
    {
        var ra = res.Headers.RetryAfter;
        if (ra is null)
            return null;

        return ra.Delta ?? (ra.Date is { } date ? date - now : null);
    }

    private static string GetHost(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.IdnHost : url;

    private static TimeSpan Doubled(TimeSpan first, int step, TimeSpan max)
    {
        var ticks = first.Ticks << Math.Clamp(step, 0, MAX_BACKOFF_SHIFT);
        return ticks >= max.Ticks ? max : new TimeSpan(ticks);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max)
        => value < min ? min : value > max ? max : value;
}
