namespace Nadeko.Common;

public sealed class QueueDropWarning
{
    private readonly string _queue;
    private readonly long _intervalMs;
    private long _lastWarning;
    private long _dropped;

    public QueueDropWarning(string queue, int intervalMs = 60_000)
    {
        _queue = queue;
        _intervalMs = intervalMs;
        _lastWarning = Environment.TickCount64 - intervalMs;
    }

    public void Dropped()
    {
        Interlocked.Increment(ref _dropped);

        // a flood fills the queue for a long time, so the log gets one line for each interval
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastWarning);
        if (now - last < _intervalMs || Interlocked.CompareExchange(ref _lastWarning, now, last) != last)
            return;

        Log.Warning("The {Queue} queue is full, dropped {Count} items since the last warning",
            _queue,
            Interlocked.Exchange(ref _dropped, 0));
    }
}
