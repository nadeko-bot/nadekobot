using System.Threading.Channels;

namespace NadekoBot.Services;

// Hands out one item per server in turn, so a raid on one server can not delay the other servers.
public sealed class GreetQueue<T>
{
    public const int MAX_PENDING_PER_GUILD = 10;

    private sealed class GuildItems
    {
        public readonly System.Collections.Concurrent.ConcurrentQueue<T> Items = new();
        public int Count;
        public int Dropped;
    }

    private readonly ConcurrentDictionary<ulong, GuildItems> _guilds = new();

    // a server id is in this channel at most once, so its size is limited by the number of servers
    private readonly Channel<ulong> _ready = Channel.CreateUnbounded<ulong>(new()
    {
        SingleReader = true
    });

    // firstDrop is true only for the first refused item since the server's queue was last empty
    public bool TryEnqueue(ulong guildId, T item, out bool firstDrop)
    {
        var guild = _guilds.GetOrAdd(guildId, static _ => new());

        var count = Interlocked.Increment(ref guild.Count);
        if (count > MAX_PENDING_PER_GUILD)
        {
            Interlocked.Decrement(ref guild.Count);
            firstDrop = Interlocked.Increment(ref guild.Dropped) == 1;
            return false;
        }

        firstDrop = false;

        guild.Items.Enqueue(item);

        if (count == 1)
            _ready.Writer.TryWrite(guildId);

        return true;
    }

    public async ValueTask<(ulong GuildId, T Item)> DequeueAsync(CancellationToken cancel = default)
    {
        var guildId = await _ready.Reader.ReadAsync(cancel);
        var guild = _guilds[guildId];

        // the count is raised before the item is added, so the item can arrive a moment later
        T? item;
        var spin = new SpinWait();
        while (!guild.Items.TryDequeue(out item))
            spin.SpinOnce();

        if (Interlocked.Decrement(ref guild.Count) > 0)
            _ready.Writer.TryWrite(guildId);
        else
            Volatile.Write(ref guild.Dropped, 0);

        return (guildId, item);
    }
}
