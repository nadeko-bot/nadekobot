using NadekoBot.Common.ModuleBehaviors;

namespace NadekoBot.Common;

public sealed class DownloadTracker(IntentState intents, DiscordSocketClient client, TimeProvider? timeProvider = null)
    : INService, IReadyExecutor
{
    // Above this size the memory and the gateway traffic of a full member list cost too much on the shared shards.
    public const int MAX_MEMBERS = 10_000;

    // Downloaded members stay in the cache, so the downloads of one shard may hold at most this many.
    public const int MAX_SHARD_MEMBERS = 250_000;
    public const int MAX_PARALLEL = 3;
    public const int MAX_REQUESTS_PER_MINUTE = 20;

    public static readonly TimeSpan Cooldown = TimeSpan.FromHours(1);

    // Discord allows one full member request for each server every 30 seconds.
    public static readonly TimeSpan MinRetry = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan SlotWait = TimeSpan.FromSeconds(10);

    // Discord.Net waits for the member list with no time limit, and Discord does not answer a rate limited request.
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    // A downloaded member list which no command used for this long leaves the cache.
    public static readonly TimeSpan IdleTime = TimeSpan.FromHours(1);

    private static readonly TimeSpan _requestWindow = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan _evictInterval = TimeSpan.FromMinutes(10);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<ulong, Task> _inFlight = new();
    private readonly SemaphoreSlim _slots = new(MAX_PARALLEL, MAX_PARALLEL);
    private readonly Lock _windowLock = new();
    private ConcurrentDictionary<ulong, long> _lastStart = new();
    private ConcurrentDictionary<ulong, DownloadedGuild> _downloaded = new();
    private long _cachedMembers;
    private long _windowStart;
    private int _windowCount;

    private readonly record struct DownloadedGuild(int Members, long LastUsed);

    public Task OnReadyAsync()
    {
        client.Ready += OnNewSessionAsync;
        _ = Task.Run(EvictLoopInternalAsync);
        return Task.CompletedTask;
    }

    // A new gateway session fills the member cache again from nothing.
    private Task OnNewSessionAsync()
    {
        Interlocked.Exchange(ref _downloaded, new());
        Interlocked.Exchange(ref _lastStart, new());
        Interlocked.Exchange(ref _cachedMembers, 0);
        return Task.CompletedTask;
    }

    // True when the cache holds every member of the server. With force (owner only) the size limits and the long cooldown do not apply.
    public Task<bool> EnsureUsersDownloadedAsync(IGuild guild, bool force = false)
        => guild is SocketGuild sg
            ? EnsureAsync(sg.Id, sg.MemberCount, sg, static g => g.HasAllMembers, static g => g.DownloadUsersAsync(), force)
            : Task.FromResult(false);

    public async Task<bool> EnsureAsync<TState>(
        ulong guildId,
        int memberCount,
        TState state,
        Func<TState, bool> hasAllMembers,
        Func<TState, Task> download,
        bool force)
    {
        if (hasAllMembers(state))
        {
            TouchInternal(guildId);
            return true;
        }

        // Discord sends the member list only to a bot with the Server Members intent.
        if (!intents.Members || (!force && memberCount > MAX_MEMBERS))
            return false;

        if (_inFlight.TryGetValue(guildId, out var running))
        {
            await running;
            return hasAllMembers(state);
        }

        if (_lastStart.TryGetValue(guildId, out var last)
            && _time.GetElapsedTime(last) < (force ? MinRetry : Cooldown))
            return hasAllMembers(state);

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_inFlight.TryAdd(guildId, done.Task))
        {
            if (_inFlight.TryGetValue(guildId, out running))
                await running;

            return hasAllMembers(state);
        }

        try
        {
            await DownloadInternalAsync(guildId, memberCount, state, download, force);
        }
        finally
        {
            _inFlight.TryRemove(new KeyValuePair<ulong, Task>(guildId, done.Task));
            done.TrySetResult();
        }

        return hasAllMembers(state);
    }

    // Removes the member lists which no command used for IdleTime. Returns how many it removed.
    public int EvictIdle(Action<ulong> purge)
    {
        var downloaded = _downloaded;
        var evicted = 0;
        foreach (var (guildId, entry) in downloaded)
        {
            if (_time.GetElapsedTime(entry.LastUsed) < IdleTime || _inFlight.ContainsKey(guildId))
                continue;

            if (!downloaded.TryRemove(new KeyValuePair<ulong, DownloadedGuild>(guildId, entry)))
                continue;

            purge(guildId);
            Interlocked.Add(ref _cachedMembers, -entry.Members);
            evicted++;
        }

        return evicted;
    }

    private async Task DownloadInternalAsync<TState>(
        ulong guildId,
        int memberCount,
        TState state,
        Func<TState, Task> download,
        bool force)
    {
        var hasSlot = false;
        try
        {
            using (var wait = new CancellationTokenSource(SlotWait, _time))
                await _slots.WaitAsync(wait.Token);

            hasSlot = true;

            // Without a request to Discord there is no cooldown, so a refused download can try again soon.
            var now = _time.GetTimestamp();
            if (!TryTakeRequestInternal(now) || !TryReserveInternal(guildId, memberCount, now, force))
                return;

            _lastStart[guildId] = now;

            using var cts = new CancellationTokenSource(Timeout, _time);
            await download(state).WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            if (hasSlot)
                Log.Warning("Member download for server {GuildId} did not finish in {Timeout}", guildId, Timeout);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Member download for server {GuildId} failed", guildId);
        }
        finally
        {
            if (hasSlot)
                _slots.Release();
        }
    }

    private bool TryTakeRequestInternal(long now)
    {
        lock (_windowLock)
        {
            if (_windowCount == 0 || _time.GetElapsedTime(_windowStart, now) >= _requestWindow)
            {
                _windowStart = now;
                _windowCount = 0;
            }

            if (_windowCount >= MAX_REQUESTS_PER_MINUTE)
                return false;

            _windowCount++;
            return true;
        }
    }

    private bool TryReserveInternal(ulong guildId, int memberCount, long now, bool force)
    {
        var downloaded = _downloaded;
        if (downloaded.TryGetValue(guildId, out var entry))
        {
            downloaded[guildId] = entry with { LastUsed = now };
            return true;
        }

        if (Interlocked.Add(ref _cachedMembers, memberCount) > MAX_SHARD_MEMBERS && !force)
        {
            Interlocked.Add(ref _cachedMembers, -memberCount);
            return false;
        }

        downloaded[guildId] = new(memberCount, now);
        return true;
    }

    private void TouchInternal(ulong guildId)
    {
        var downloaded = _downloaded;
        if (downloaded.TryGetValue(guildId, out var entry))
            downloaded[guildId] = entry with { LastUsed = _time.GetTimestamp() };
    }

    private async Task EvictLoopInternalAsync()
    {
        using var timer = new PeriodicTimer(_evictInterval, _time);
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                EvictIdle(PurgeGuildInternal);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error removing idle member lists from the cache");
            }
        }
    }

    // Keeps the bot, members in voice and members who are online, as the cache held them before the download.
    private void PurgeGuildInternal(ulong guildId)
    {
        var botId = client.CurrentUser?.Id ?? 0;
        client.GetGuild(guildId)?.PurgeUserCache(u => u.Id != botId
                                                     && u.VoiceChannel is null
                                                     && u.Status == UserStatus.Offline);
    }
}
