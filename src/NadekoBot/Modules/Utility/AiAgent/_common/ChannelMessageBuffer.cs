namespace NadekoBot.Modules.Utility.AiAgent;

public readonly record struct MessageSnapshot(
    ulong MessageId,
    ulong AuthorId,
    string AuthorName,
    string Content,
    DateTimeOffset Timestamp);

// Fixed-size ring buffer of the recent messages of one channel.
public sealed class ChannelMessageBuffer
{
    private readonly MessageSnapshot[] _buffer;
    private readonly int _capacity;
    private int _count;
    private int _writeIndex;
    private readonly Lock _lock = new();

    // Drives the idle expiry of the buffer.
    public DateTime LastAccessedUtc { get; private set; }

    public ChannelMessageBuffer(int capacity)
    {
        _capacity = capacity;
        _buffer = new MessageSnapshot[capacity];
        _count = 0;
        _writeIndex = 0;
        LastAccessedUtc = DateTime.UtcNow;
    }

    // A full buffer overwrites the oldest entry.
    public void Push(MessageSnapshot snapshot)
    {
        lock (_lock)
        {
            _buffer[_writeIndex] = snapshot;
            _writeIndex = (_writeIndex + 1) % _capacity;
            if (_count < _capacity)
                _count++;
            LastAccessedUtc = DateTime.UtcNow;
        }
    }

    // Returns the messages in chronological order.
    public MessageSnapshot[] GetMessages()
    {
        lock (_lock)
        {
            LastAccessedUtc = DateTime.UtcNow;

            if (_count == 0)
                return [];

            var result = new MessageSnapshot[_count];

            if (_count < _capacity)
            {
                Array.Copy(_buffer, 0, result, 0, _count);
            }
            else
            {
                var oldestIndex = _writeIndex;
                var firstChunkLen = _capacity - oldestIndex;
                Array.Copy(_buffer, oldestIndex, result, 0, firstChunkLen);
                Array.Copy(_buffer, 0, result, firstChunkLen, oldestIndex);
            }

            return result;
        }
    }

    public int Count
    {
        get { lock (_lock) return _count; }
    }

    public bool TryRemove(ulong messageId)
    {
        lock (_lock)
        {
            if (_count == 0)
                return false;

            var oldestIndex = _count < _capacity ? 0 : _writeIndex;
            var foundAt = -1;

            for (var i = 0; i < _count; i++)
            {
                var idx = (oldestIndex + i) % _capacity;
                if (_buffer[idx].MessageId == messageId)
                {
                    foundAt = i;
                    break;
                }
            }

            if (foundAt < 0)
                return false;

            // Rebuilt, because juggling the write index of a wrapped ring after a removal is worse.
            var remaining = _count - 1;
            var rebuilt = new MessageSnapshot[_capacity];
            var dst = 0;
            for (var i = 0; i < _count; i++)
            {
                if (i == foundAt)
                    continue;
                var src = (oldestIndex + i) % _capacity;
                rebuilt[dst++] = _buffer[src];
            }

            Array.Copy(rebuilt, _buffer, _capacity);
            _count = remaining;
            _writeIndex = remaining % _capacity;
            LastAccessedUtc = DateTime.UtcNow;
            return true;
        }
    }

    // Every value is escaped, so Discord markup cannot break the structure.
    public string? BuildHistoryXml(ulong channelId, string channelName, ulong excludeMessageId)
    {
        var snapshots = GetMessages();
        if (snapshots.Length == 0)
            return null;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"<channel_history channel_id=\"{channelId}\" channel_name=\"{PromptSanitizer.XmlEscape(channelName)}\">");

        foreach (var s in snapshots)
        {
            if (s.MessageId == excludeMessageId)
                continue;

            sb.AppendLine($"<msg id=\"{s.MessageId}\" author=\"{PromptSanitizer.XmlEscape(s.AuthorName)}\" author_id=\"{s.AuthorId}\" time=\"{s.Timestamp.ToUnixTimeSeconds()}\">{PromptSanitizer.XmlEscape(s.Content)}</msg>");
        }

        sb.Append("</channel_history>");
        return sb.ToString();
    }
}
