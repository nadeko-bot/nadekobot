using System.Threading.Channels;

namespace NadekoBot.Modules.Utility.AiAgent;

public readonly record struct SessionButton(ulong GuildId, ulong ChannelId, ulong MessageId);

// The End session button shows the member that the bot reads their messages, so each end of a session queues it for removal.
public sealed class ConversationWindowTracker(TimeProvider? timeProvider = null) : INService
{
    public const int MAX_STALE = 256;

    private const long STALE_WARNING_INTERVAL_MS = 60_000;

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<(ulong UserId, ulong ChannelId), Window> _windows = new();

    private readonly Channel<SessionButton> _stale = Channel.CreateBounded<SessionButton>(new BoundedChannelOptions(MAX_STALE)
    {
        SingleReader = false,
        SingleWriter = false
    });

    private long _lastStaleWarning;
    private int _droppedStale;

    public ChannelReader<SessionButton> Stale
        => _stale.Reader;

    public void Open(ulong userId, SessionButton button)
    {
        var key = (userId, button.ChannelId);
        var window = new Window(button, _time.GetTimestamp());
        while (true)
        {
            if (_windows.TryGetValue(key, out var old))
            {
                if (!_windows.TryUpdate(key, window, old))
                    continue;

                if (old.Button.MessageId != button.MessageId)
                    QueueStaleInternal(old.Button);

                return;
            }

            if (_windows.TryAdd(key, window))
                return;
        }
    }

    public bool Touch(ulong userId, ulong channelId)
    {
        var key = (userId, channelId);
        while (_windows.TryGetValue(key, out var old))
        {
            if (_windows.TryUpdate(key, new Window(old.Button, _time.GetTimestamp()), old))
                return true;
        }

        return false;
    }

    // A session does not end while a request of the member runs, because its answer starts the time again.
    public bool IsActive(ulong userId, ulong channelId, double windowSeconds, bool busy)
    {
        var key = (userId, channelId);
        if (!_windows.TryGetValue(key, out var window))
            return false;

        if (busy || !IsExpiredInternal(window, windowSeconds))
            return true;

        RemoveInternal(key, window);
        return false;
    }

    public bool Close(ulong userId, ulong channelId)
    {
        if (!_windows.TryRemove((userId, channelId), out var window))
            return false;

        QueueStaleInternal(window.Button);
        return true;
    }

    // The click on a button removes that button itself, so it does not go to the worker.
    public bool CloseAll(ulong userId, ulong keepMessageId = 0)
    {
        var closed = false;
        foreach (var (key, window) in _windows)
        {
            if (key.UserId != userId || !_windows.TryRemove(new KeyValuePair<(ulong, ulong), Window>(key, window)))
                continue;

            closed = true;
            if (window.Button.MessageId != keepMessageId)
                QueueStaleInternal(window.Button);
        }

        return closed;
    }

    public void CloseExpired(double windowSeconds, Func<ulong, bool> isBusy)
    {
        foreach (var (key, window) in _windows)
        {
            if (IsExpiredInternal(window, windowSeconds) && !isBusy(key.UserId))
                RemoveInternal(key, window);
        }
    }

    // For a button whose session never opened or is already closed.
    public void QueueRemoval(SessionButton button)
        => QueueStaleInternal(button);

    private bool IsExpiredInternal(Window window, double windowSeconds)
        => _time.GetElapsedTime(window.Opened).TotalSeconds > windowSeconds;

    // Removes only this window, so a session which opened again at the same time stays.
    private void RemoveInternal((ulong, ulong) key, Window window)
    {
        if (_windows.TryRemove(new KeyValuePair<(ulong, ulong), Window>(key, window)))
            QueueStaleInternal(window.Button);
    }

    private void QueueStaleInternal(SessionButton button)
    {
        if (_stale.Writer.TryWrite(button))
            return;

        Interlocked.Increment(ref _droppedStale);
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastStaleWarning);
        if (now - last < STALE_WARNING_INTERVAL_MS || Interlocked.CompareExchange(ref _lastStaleWarning, now, last) != last)
            return;

        Log.Warning("The queue of ended AI agent sessions is full, so {Count} End session buttons stay on their messages",
            Interlocked.Exchange(ref _droppedStale, 0));
    }

    // A class, so an update and a remove compare the instance which they read.
    private sealed class Window(SessionButton button, long opened)
    {
        public SessionButton Button { get; } = button;

        public long Opened { get; } = opened;
    }
}
