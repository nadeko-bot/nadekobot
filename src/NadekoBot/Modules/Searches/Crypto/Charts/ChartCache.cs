namespace NadekoBot.Modules.Searches.Charts;

// The last few chart images of one shard. An image is valid only for the last candle which it shows.
public sealed class ChartCache(int capacity, TimeSpan expiry, TimeProvider time)
{
    private sealed class Entry(string key, Candle last, byte[] image, long expires)
    {
        public readonly string Key = key;
        public readonly Candle Last = last;
        public readonly byte[] Image = image;
        public readonly long Expires = expires;
    }

    private readonly Entry?[] _entries = new Entry?[capacity];
    private int _next = -1;

    public byte[]? Get(string key, in Candle last)
    {
        var now = time.GetUtcNow().UtcTicks;
        for (var i = 0; i < _entries.Length; i++)
        {
            var entry = Volatile.Read(ref _entries[i]);
            if (entry is not null
                && entry.Expires > now
                && entry.Last == last
                && string.Equals(entry.Key, key, StringComparison.Ordinal))
                return entry.Image;
        }

        return null;
    }

    public void Set(string key, in Candle last, byte[] image)
    {
        var index = (int)((uint)Interlocked.Increment(ref _next) % (uint)_entries.Length);
        Volatile.Write(ref _entries[index], new Entry(key, last, image, (time.GetUtcNow() + expiry).UtcTicks));
    }
}
