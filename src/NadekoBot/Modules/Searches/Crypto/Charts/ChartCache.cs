namespace NadekoBot.Modules.Searches.Charts;

// The last few chart images of one shard. An image is valid only for the frame and the last candle which it shows.
public sealed class ChartCache(int capacity, TimeSpan expiry, TimeProvider time)
{
    private sealed class Entry(string key, ChartFrame frame, Candle last, byte[] image, long expires)
    {
        public readonly string Key = key;
        public readonly ChartFrame Frame = frame;
        public readonly Candle Last = last;
        public readonly byte[] Image = image;
        public readonly long Expires = expires;
    }

    private readonly Entry?[] _entries = new Entry?[capacity];
    private int _next = -1;

    public byte[]? Get(string key, ChartFrame frame, in Candle last)
    {
        var now = time.GetUtcNow().UtcTicks;
        for (var i = 0; i < _entries.Length; i++)
        {
            var entry = Volatile.Read(ref _entries[i]);
            if (entry is not null
                && entry.Expires > now
                && entry.Frame == frame
                && entry.Last == last
                && string.Equals(entry.Key, key, StringComparison.Ordinal))
                return entry.Image;
        }

        return null;
    }

    public void Set(string key, ChartFrame frame, in Candle last, byte[] image)
    {
        var index = (int)((uint)Interlocked.Increment(ref _next) % (uint)_entries.Length);
        Volatile.Write(ref _entries[index], new Entry(key, frame, last, image, (time.GetUtcNow() + expiry).UtcTicks));
    }
}
