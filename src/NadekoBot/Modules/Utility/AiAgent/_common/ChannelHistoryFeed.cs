namespace NadekoBot.Modules.Utility.AiAgent;

// The snapshot is sent once and later steps only get the new messages,
// so the earlier messages of the request never change and the provider can reuse its prompt cache.
public sealed class ChannelHistoryFeed
{
    private readonly ChannelMessageBuffer _buffer;
    private readonly ulong _channelId;
    private readonly string _channelName;
    private readonly ulong _triggerMessageId;
    private ulong _cursor;

    public ChannelHistoryFeed(ChannelMessageBuffer buffer, ulong channelId, string channelName, ulong triggerMessageId)
    {
        _buffer = buffer;
        _channelId = channelId;
        _channelName = channelName;
        _triggerMessageId = triggerMessageId;
        _cursor = triggerMessageId;
    }

    public string? GetSnapshot()
    {
        var xml = _buffer.BuildHistoryXml(_channelId, _channelName, _triggerMessageId, out var last);
        _cursor = Math.Max(_cursor, last);
        return xml;
    }

    public string? GetUpdate()
        => _buffer.BuildUpdateXml(_channelId, _cursor, _triggerMessageId, out _cursor);
}
