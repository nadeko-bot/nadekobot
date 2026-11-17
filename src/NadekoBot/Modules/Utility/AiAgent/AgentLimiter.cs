namespace NadekoBot.Modules.Utility.AiAgent;

// A limit makes a request wait; it does not drop it.
public sealed class AgentLimiter(TimeProvider? timeProvider = null) : INService
{
    public const int MEMBER_BURST = 5;
    public static readonly TimeSpan MemberRefill = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan MemberMaxWait = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MemberNoticeAfter = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan MemberNoticeInterval = TimeSpan.FromMinutes(1);

    public const int GUILD_CONCURRENT_REQUESTS = 3;
    public const int GUILD_MAX_WAITING = 10;
    public static readonly TimeSpan GuildWait = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan GuildNoticeInterval = TimeSpan.FromSeconds(30);

    public const int GUILD_SENDS_BURST = 10;
    public static readonly TimeSpan GuildSendsRefill = TimeSpan.FromSeconds(6);
    public static readonly TimeSpan SendMaxWait = TimeSpan.FromSeconds(12);

    // Reads, edits and deletes of the message tools.
    public const int GUILD_REST_BURST = 10;
    public static readonly TimeSpan GuildRestRefill = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan RestMaxWait = TimeSpan.FromSeconds(10);

    // The reaction which tells a member that the server is busy.
    public const int GUILD_REACTIONS_BURST = 5;
    public static readonly TimeSpan GuildReactionsRefill = TimeSpan.FromSeconds(1);

    public const int GUILD_STATUS_BURST = 5;
    public static readonly TimeSpan GuildStatusRefill = TimeSpan.FromSeconds(2);
    public const int SHARD_STATUS_BURST = 5;
    public static readonly TimeSpan ShardStatusRefill = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan IdleExpiry = TimeSpan.FromMinutes(10);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<ulong, MemberGate> _members = new();
    private readonly ConcurrentDictionary<ulong, GuildGate> _guilds = new();
    private readonly TokenBucket _shardStatus = new(SHARD_STATUS_BURST, ShardStatusRefill, timeProvider ?? TimeProvider.System);

    public async Task<bool> WaitForMemberAsync(ulong userId, Func<Task> notice, CancellationToken ct)
    {
        var gate = _members.GetOrAdd(userId, static (_, time) => new(time), _time);
        gate.LastUsed = _time.GetTimestamp();
        if (gate.Requests.TryTake())
            return true;

        if (gate.Requests.WaitTime() > MemberNoticeAfter && gate.Notice.TryNotify())
            _ = notice();

        return await gate.Requests.TakeAsync(1, MemberMaxWait, ct);
    }

    public void RefundMember(ulong userId)
    {
        if (_members.TryGetValue(userId, out var gate))
            gate.Requests.Refund();
    }

    public async Task<GuildSlot?> EnterGuildAsync(ulong guildId, Func<Task> notice, CancellationToken ct)
    {
        var gate = GetGuildInternal(guildId);
        var requests = gate.Requests;
        if (requests.Wait(0, CancellationToken.None))
            return new(requests);

        // A crowd beyond this gets "busy" at once instead of a long wait which ends the same way.
        if (Interlocked.Increment(ref gate.Waiting) > GUILD_MAX_WAITING)
        {
            Interlocked.Decrement(ref gate.Waiting);
            return null;
        }

        try
        {
            if (gate.WaitNotice.TryNotify())
                _ = notice();

            return await requests.WaitAsync(GuildWait, ct) ? new(requests) : null;
        }
        finally
        {
            Interlocked.Decrement(ref gate.Waiting);
        }
    }

    public bool TryNotifyBusy(ulong guildId)
        => GetGuildInternal(guildId).BusyError.TryNotify();

    public bool TryTakeReaction(ulong guildId)
        => GetGuildInternal(guildId).Reactions.TryTake();

    public Task<bool> TakeSendAsync(ulong guildId, int count, CancellationToken ct)
        => GetGuildInternal(guildId).Sends.TakeAsync(count, SendMaxWait, ct);

    public Task<bool> TakeRestAsync(ulong guildId, int count, CancellationToken ct)
        => GetGuildInternal(guildId).Rest.TakeAsync(count, RestMaxWait, ct);

    // The steps message skips an edit when the server or the shard edits too often; the next step brings it up to date.
    public bool TryTakeStatusEdit(ulong guildId)
    {
        if (!_shardStatus.TryTake())
            return false;

        if (GetGuildInternal(guildId).StatusEdits.TryTake())
            return true;

        _shardStatus.Refund();
        return false;
    }

    public void RemoveIdle()
    {
        var now = _time.GetTimestamp();
        foreach (var (id, gate) in _members)
        {
            if (_time.GetElapsedTime(gate.LastUsed, now) > IdleExpiry && gate.Requests.IsFull)
                _members.TryRemove(new KeyValuePair<ulong, MemberGate>(id, gate));
        }

        // A request which took a slot of a removed gate frees it through its own GuildSlot.
        foreach (var (id, gate) in _guilds)
        {
            if (_time.GetElapsedTime(gate.LastUsed, now) > IdleExpiry && gate.IsIdle)
                _guilds.TryRemove(new KeyValuePair<ulong, GuildGate>(id, gate));
        }
    }

    private GuildGate GetGuildInternal(ulong guildId)
    {
        var gate = _guilds.GetOrAdd(guildId, static (_, time) => new(time), _time);
        gate.LastUsed = _time.GetTimestamp();
        return gate;
    }

    private sealed class MemberGate(TimeProvider time)
    {
        public TokenBucket Requests { get; } = new(MEMBER_BURST, MemberRefill, time);

        public NoticeLimiter Notice { get; } = new(MemberNoticeInterval, time);

        public long LastUsed { get; set; }
    }

    private sealed class GuildGate(TimeProvider time)
    {
        public SemaphoreSlim Requests { get; } = new(GUILD_CONCURRENT_REQUESTS, GUILD_CONCURRENT_REQUESTS);

        public int Waiting;

        public TokenBucket Sends { get; } = new(GUILD_SENDS_BURST, GuildSendsRefill, time);

        public TokenBucket Rest { get; } = new(GUILD_REST_BURST, GuildRestRefill, time);

        public TokenBucket StatusEdits { get; } = new(GUILD_STATUS_BURST, GuildStatusRefill, time);

        public TokenBucket Reactions { get; } = new(GUILD_REACTIONS_BURST, GuildReactionsRefill, time);

        public NoticeLimiter WaitNotice { get; } = new(GuildNoticeInterval, time);

        public NoticeLimiter BusyError { get; } = new(GuildNoticeInterval, time);

        public long LastUsed { get; set; }

        public bool IsIdle
            => Requests.CurrentCount == GUILD_CONCURRENT_REQUESTS
               && Volatile.Read(ref Waiting) == 0
               && Sends.IsFull
               && Rest.IsFull
               && StatusEdits.IsFull
               && Reactions.IsFull;
    }
}

public sealed class GuildSlot(SemaphoreSlim semaphore) : IDisposable
{
    private SemaphoreSlim? _semaphore = semaphore;

    public void Dispose()
        => Interlocked.Exchange(ref _semaphore, null)?.Release();
}

public sealed class TokenBucket(int capacity, TimeSpan refill, TimeProvider time)
{
    private static readonly TimeSpan _minWait = TimeSpan.FromMilliseconds(10);

    private readonly Lock _lock = new();
    private double _tokens = capacity;
    private long _last = time.GetTimestamp();

    public bool TryTake(int count = 1)
    {
        lock (_lock)
        {
            RefillInternal();
            if (_tokens < count)
                return false;

            _tokens -= count;
            return true;
        }
    }

    public void Refund()
    {
        lock (_lock)
            _tokens = Math.Min(capacity, _tokens + 1);
    }

    // Waits in a loop, because another waiter can take the tokens which this one waited for.
    public async Task<bool> TakeAsync(int count, TimeSpan maxWait, CancellationToken ct)
    {
        // More than the capacity would never fit.
        count = Math.Clamp(count, 1, capacity);
        var start = time.GetTimestamp();
        while (!TryTake(count))
        {
            var wait = WaitTime(count);
            if (time.GetElapsedTime(start) + wait > maxWait)
                return false;

            await Task.Delay(wait < _minWait ? _minWait : wait, time, ct);
        }

        return true;
    }

    public TimeSpan WaitTime(int count = 1)
    {
        lock (_lock)
        {
            RefillInternal();
            return _tokens >= count ? TimeSpan.Zero : refill * (count - _tokens);
        }
    }

    public bool IsFull
    {
        get
        {
            lock (_lock)
            {
                RefillInternal();
                return _tokens >= capacity;
            }
        }
    }

    private void RefillInternal()
    {
        var now = time.GetTimestamp();
        _tokens = Math.Min(capacity, _tokens + time.GetElapsedTime(_last, now) / refill);
        _last = now;
    }
}

// Allows one notice in each interval, so a crowd of waiting requests posts one message, not one each.
public sealed class NoticeLimiter(TimeSpan interval, TimeProvider time)
{
    private const long NEVER = long.MinValue;
    private long _last = NEVER;

    public bool TryNotify()
    {
        var now = time.GetTimestamp();
        var last = Interlocked.Read(ref _last);
        if (last != NEVER && time.GetElapsedTime(last, now) < interval)
            return false;

        return Interlocked.CompareExchange(ref _last, now, last) == last;
    }
}
