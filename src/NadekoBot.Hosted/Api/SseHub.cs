using System.Threading.Channels;

namespace NadekoBot.Hosted;

// One SSE frame, encoded once and shared by the ring buffer and every subscriber.
public readonly record struct FeedFrame(long Id, byte[] Bytes)
{
    private const long KEEP_ALIVE_ID = 0;

    // A comment line, which keeps proxies with short idle timeouts from closing the stream.
    public static readonly FeedFrame KeepAlive = new(KEEP_ALIVE_ID, ": ka\n\n"u8.ToArray());

    public bool IsKeepAlive
        => Id == KEEP_ALIVE_ID;
}

// The recent frames and the open streams of one SSE route. Ids must start above 0 and go up.
public sealed class SseHub(int bufferCapacity, int maxSubscribers, int subscriberCapacity)
{
    private readonly FeedRingBuffer _buffer = new(bufferCapacity);
    // Copy-on-write: subscribing is rare, and publishing reads the array with no lock and no allocation.
    private FeedSubscriber[] _subscribers = [];
    private long _nextSubscriberId;

    public int BufferCapacity
        => _buffer.Capacity;

    public bool HasSubscribers
        => Volatile.Read(ref _subscribers).Length > 0;

    public FeedSubscription? Subscribe()
    {
        // Saves the allocations for a rejected stream; the loop below enforces the cap.
        if (Volatile.Read(ref _subscribers).Length >= maxSubscribers)
            return null;

        var channel = Channel.CreateBounded<FeedFrame>(new BoundedChannelOptions(subscriberCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });
        var subscriber = new FeedSubscriber(Interlocked.Increment(ref _nextSubscriberId), channel);

        while (true)
        {
            var current = Volatile.Read(ref _subscribers);
            if (current.Length >= maxSubscribers)
                return null;

            var next = new FeedSubscriber[current.Length + 1];
            current.CopyTo(next, 0);
            next[^1] = subscriber;
            if (Interlocked.CompareExchange(ref _subscribers, next, current) == current)
                return new FeedSubscription(this, subscriber.Id, channel.Reader);
        }
    }

    public void Unsubscribe(long id)
    {
        while (true)
        {
            var current = Volatile.Read(ref _subscribers);
            var index = IndexOfInternal(current, id);
            if (index < 0)
                return;

            var next = new FeedSubscriber[current.Length - 1];
            current.AsSpan(0, index).CopyTo(next);
            current.AsSpan(index + 1).CopyTo(next.AsSpan(index));
            if (Interlocked.CompareExchange(ref _subscribers, next, current) != current)
                continue;

            current[index].Channel.Writer.TryComplete();
            return;
        }
    }

    private static int IndexOfInternal(FeedSubscriber[] subscribers, long id)
    {
        for (var i = 0; i < subscribers.Length; i++)
        {
            if (subscribers[i].Id == id)
                return i;
        }

        return -1;
    }

    // The frame goes into the buffer before any stream gets it, so a stream which subscribed before its snapshot misses nothing.
    // Only one thread may call Add and PublishKeepAlive, because each stream channel has a single writer.
    public void Add(in FeedFrame frame)
    {
        _buffer.Add(frame);
        PublishInternal(frame);
    }

    public void PublishKeepAlive()
        => PublishInternal(FeedFrame.KeepAlive);

    private void PublishInternal(in FeedFrame frame)
    {
        foreach (var subscriber in Volatile.Read(ref _subscribers))
            subscriber.Channel.Writer.TryWrite(frame);
    }

    public int Snapshot(long sinceId, Span<FeedFrame> destination)
        => _buffer.Snapshot(sinceId, destination);

    public int Tail(int count, Span<FeedFrame> destination, out long newestId)
        => _buffer.Tail(count, destination, out newestId);

    public void CompleteAll()
    {
        foreach (var subscriber in Interlocked.Exchange(ref _subscribers, []))
            subscriber.Channel.Writer.TryComplete();
    }
}

public readonly record struct FeedSubscriber(long Id, Channel<FeedFrame> Channel);

public sealed class FeedSubscription(SseHub hub, long id, ChannelReader<FeedFrame> reader) : IDisposable
{
    public ChannelReader<FeedFrame> Reader
        => reader;

    public void Dispose()
        => hub.Unsubscribe(id);
}

// Holds the most recent frames in id order.
public sealed class FeedRingBuffer(int capacity = FeedRingBuffer.CAPACITY)
{
    public const int CAPACITY = 256;

    private readonly Lock _lock = new();
    private readonly FeedFrame[] _items = new FeedFrame[capacity];
    private int _head;
    private int _count;

    public int Capacity
        => capacity;

    public void Add(in FeedFrame frame)
    {
        lock (_lock)
        {
            if (_count < capacity)
            {
                _items[(_head + _count) % capacity] = frame;
                _count++;
                return;
            }

            _items[_head] = frame;
            _head = (_head + 1) % capacity;
        }
    }

    public int Snapshot(long sinceId, Span<FeedFrame> destination)
    {
        lock (_lock)
        {
            var start = 0;
            while (start < _count && _items[(_head + start) % capacity].Id <= sinceId)
                start++;

            return CopyInternal(start, Math.Min(_count - start, destination.Length), destination);
        }
    }

    // The newest id lets a stream which asked for no old frames still skip the ones it gets twice.
    public int Tail(int count, Span<FeedFrame> destination, out long newestId)
    {
        lock (_lock)
        {
            newestId = _count == 0 ? 0 : _items[(_head + _count - 1) % capacity].Id;
            var length = Math.Clamp(count, 0, Math.Min(_count, destination.Length));
            return CopyInternal(_count - length, length, destination);
        }
    }

    private int CopyInternal(int start, int length, Span<FeedFrame> destination)
    {
        for (var i = 0; i < length; i++)
            destination[i] = _items[(_head + start + i) % capacity];

        return length;
    }
}
