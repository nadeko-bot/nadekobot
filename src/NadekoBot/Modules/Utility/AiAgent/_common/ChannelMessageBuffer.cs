namespace NadekoBot.Modules.Utility.AiAgent;

/// <summary>
/// Lightweight snapshot of a Discord message for the agent's channel memory
/// </summary>
public readonly record struct MessageSnapshot(
    ulong MessageId,
    ulong AuthorId,
    string AuthorName,
    string Content,
    DateTimeOffset Timestamp);

/// <summary>
/// Fixed-size ring buffer that stores recent message snapshots for a single channel.
/// Thread-safe via lock. Tracks last-accessed time for idle expiry.
/// </summary>
public sealed class ChannelMessageBuffer
{
    private readonly MessageSnapshot[] _buffer;
    private readonly int _capacity;
    private int _count;
    private int _writeIndex;
    private readonly Lock _lock = new();

    /// <summary>
    /// When the buffer was last accessed by an agent invocation (for idle expiry)
    /// </summary>
    public DateTime LastAccessedUtc { get; private set; }

    public ChannelMessageBuffer(int capacity)
    {
        _capacity = capacity;
        _buffer = new MessageSnapshot[capacity];
        _count = 0;
        _writeIndex = 0;
        LastAccessedUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Append a message to the buffer. If full, overwrites the oldest entry.
    /// </summary>
    public void Push(MessageSnapshot snapshot)
    {
        lock (_lock)
        {
            _buffer[_writeIndex] = snapshot;
            _writeIndex = (_writeIndex + 1) % _capacity;
            if (_count < _capacity)
                _count++;
        }
    }

    /// <summary>
    /// Returns all buffered messages in chronological order and touches the last-accessed timestamp.
    /// </summary>
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

    /// <summary>
    /// Number of messages currently in the buffer
    /// </summary>
    public int Count
    {
        get { lock (_lock) return _count; }
    }

    /// <summary>
    /// Builds XML-formatted channel history, excluding the specified message.
    /// All user content is XML-escaped to prevent Discord markup from breaking the structure.
    /// </summary>
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
