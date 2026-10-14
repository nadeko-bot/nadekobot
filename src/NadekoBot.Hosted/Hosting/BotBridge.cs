using Microsoft.Extensions.DependencyInjection;

namespace NadekoBot.Hosted;

public enum BotPhase
{
    NoToken,
    InvalidToken,
    Starting,
    Ready,
    Failed
}

public static class HealthStatus
{
    public const string UNCONFIGURED = "unconfigured";
    public const string NO_TOKEN = "no_token";
    public const string INVALID_TOKEN = "invalid_token";
    public const string STARTING = "starting";
    public const string OK = "ok";
    public const string DISCONNECTED = "disconnected";
    public const string FAILED = "failed";
}

public static class FailureReason
{
    public const string STARTUP_FAILED = "startup_failed";
    public const string DISALLOWED_INTENTS = "disallowed_intents";
}

public readonly record struct BotHealth(string Status, string? Reason);

public sealed class BotBridge(TimeProvider time)
{
    // Discord drops the gateway now and then; a short reconnect is not an outage.
    public static readonly TimeSpan DisconnectGrace = TimeSpan.FromSeconds(30);

    private int _phase;
    private volatile string? _failureReason;
    private volatile DiscordSocketClient? _client;
    private volatile IServiceProvider? _services;
    private volatile CommandFeedService? _feed;
    private volatile GuildActivityService? _activity;
    private long _disconnectedAt;

    public BotBridge()
        : this(TimeProvider.System)
    {
    }

    public TimeProvider Time
        => time;

    public BotPhase Phase
        => (BotPhase)Volatile.Read(ref _phase);

    public DiscordSocketClient? Client
        => _client;

    public CommandFeedService Feed
        => _feed ?? throw new InvalidOperationException("The command feed is not running");

    public CommandFeedService? FeedOrNull
        => _feed;

    public GuildActivityService Activity
        => _activity ?? throw new InvalidOperationException("The activity counter is not running");

    public GuildActivityService? ActivityOrNull
        => _activity;

    public BotHealth Health(bool hasApiKey)
    {
        if (!hasApiKey)
            return new(HealthStatus.UNCONFIGURED, null);

        return Phase switch
        {
            BotPhase.NoToken => new(HealthStatus.NO_TOKEN, null),
            BotPhase.InvalidToken => new(HealthStatus.INVALID_TOKEN, null),
            BotPhase.Starting => new(HealthStatus.STARTING, null),
            BotPhase.Failed => new(HealthStatus.FAILED, _failureReason ?? FailureReason.STARTUP_FAILED),
            _ => new(IsDisconnected() ? HealthStatus.DISCONNECTED : HealthStatus.OK, null)
        };
    }

    public void SetPhase(BotPhase phase)
        => Volatile.Write(ref _phase, (int)phase);

    public void SetFailed(string reason)
    {
        _failureReason = reason;
        SetPhase(BotPhase.Failed);
    }

    public void AttachClient(DiscordSocketClient client)
        => _client = client;

    // A gateway close during startup may already have set a final phase, which readiness must not hide.
    public bool SetReady(IServiceProvider services, CommandFeedService? feed, GuildActivityService? activity = null)
    {
        _services = services;
        _feed = feed;
        _activity = activity;
        if (Interlocked.CompareExchange(ref _phase, (int)BotPhase.Ready, (int)BotPhase.Starting)
            == (int)BotPhase.Starting)
            return true;

        _feed = null;
        feed?.Dispose();
        return false;
    }

    public void MarkDisconnected()
        => Interlocked.CompareExchange(ref _disconnectedAt, Math.Max(1, time.GetTimestamp()), 0);

    public void MarkConnected()
        => Interlocked.Exchange(ref _disconnectedAt, 0);

    private bool IsDisconnected()
    {
        var since = Interlocked.Read(ref _disconnectedAt);
        return since != 0 && time.GetElapsedTime(since) >= DisconnectGrace;
    }

    public T Get<T>() where T : notnull
        => (_services ?? throw new InvalidOperationException("The bot is not ready")).GetRequiredService<T>();

    public IEnumerable<T> GetAll<T>()
        => (_services ?? throw new InvalidOperationException("The bot is not ready")).GetServices<T>();
}
