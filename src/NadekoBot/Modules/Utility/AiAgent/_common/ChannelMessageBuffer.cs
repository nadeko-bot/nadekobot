namespace NadekoBot.Modules.Utility.AiAgent;

// Quoted holds the text of others in the message (a reply excerpt, link embeds, file and sticker names), empty when there is none.
public readonly record struct MessageSnapshot(
    ulong MessageId,
    ulong AuthorId,
    string AuthorName,
    string Content,
    DateTimeOffset Timestamp,
    string Quoted = "");

public sealed class ChannelMessageBuffer
{
    private readonly MessageSnapshot[] _buffer;
    private readonly int _capacity;
    private int _count;
    private int _writeIndex;
    private readonly Lock _lock = new();

    public DateTime LastAccessedUtc { get; private set; }

    public ChannelMessageBuffer(int capacity)
    {
        _capacity = capacity;
        _buffer = new MessageSnapshot[capacity];
        _count = 0;
        _writeIndex = 0;
        LastAccessedUtc = DateTime.UtcNow;
    }

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
    public string? BuildHistoryXml(
        ulong channelId,
        string channelName,
        HashSet<ulong> sentIds,
        List<string>? contents = null,
        ulong memberId = 0)
    {
        var snapshots = GetMessages();
        if (snapshots.Length == 0)
            return null;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"<channel_history channel_id=\"{channelId}\" channel_name=\"{PromptSanitizer.XmlEscape(channelName)}\">");

        foreach (ref readonly var s in snapshots.AsSpan())
        {
            if (!sentIds.Add(s.MessageId))
                continue;

            AppendMessageXml(sb, in s);
            AddUntrustedInternal(contents, in s, memberId);
        }

        sb.Append("</channel_history>");
        return sb.ToString();
    }

    // Uses the set of sent ids and not the highest id, because the gateway can deliver an older message late.
    public string? BuildUpdateXml(ulong channelId, HashSet<ulong> sentIds, List<string>? contents = null, ulong memberId = 0)
    {
        var snapshots = GetMessages();

        System.Text.StringBuilder? sb = null;
        foreach (ref readonly var s in snapshots.AsSpan())
        {
            if (!sentIds.Add(s.MessageId))
                continue;

            sb ??= new System.Text.StringBuilder()
                .AppendLine($"<channel_update channel_id=\"{channelId}\" note=\"messages posted in the channel since your previous step\">");
            AppendMessageXml(sb, in s);
            AddUntrustedInternal(contents, in s, memberId);
        }

        return sb?.Append("</channel_update>").ToString();
    }

    // The member chose the words of their own messages, but not what they quote.
    private static void AddUntrustedInternal(List<string>? contents, in MessageSnapshot s, ulong memberId)
    {
        if (s.AuthorId != memberId)
            contents?.Add(UntrustedTextInternal(in s));
        else if (s.Quoted.Length > 0)
            contents?.Add(s.Quoted);
    }

    // The model reads the author too, so a name or id which only an author attribute holds is untrusted as well.
    private static string UntrustedTextInternal(in MessageSnapshot s)
        => $"{s.AuthorName} <@{s.AuthorId}> {s.Content}";

    private static void AppendMessageXml(System.Text.StringBuilder sb, in MessageSnapshot s)
        => sb.AppendLine($"<msg id=\"{s.MessageId}\" author=\"{PromptSanitizer.XmlEscape(s.AuthorName)}\" author_id=\"{s.AuthorId}\" time=\"{s.Timestamp.ToUnixTimeSeconds()}\">{PromptSanitizer.XmlEscape(s.Content)}</msg>");
}
