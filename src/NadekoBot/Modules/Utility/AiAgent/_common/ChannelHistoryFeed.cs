namespace NadekoBot.Modules.Utility.AiAgent;

// The snapshot is sent once and later steps only get the new messages,
// so the earlier messages of the request never change and the provider can reuse its prompt cache.
// One session uses it one step at a time, so the set needs no lock.
public sealed class ChannelHistoryFeed
{
    private readonly ChannelMessageBuffer _buffer;
    private readonly ulong _channelId;
    private readonly string _channelName;
    private readonly HashSet<ulong> _sentIds;

    public ChannelHistoryFeed(ChannelMessageBuffer buffer, ulong channelId, string channelName, ulong triggerMessageId)
    {
        _buffer = buffer;
        _channelId = channelId;
        _channelName = channelName;
        _sentIds = [triggerMessageId];
    }

    public string? GetSnapshot()
        => _buffer.BuildHistoryXml(_channelId, _channelName, _sentIds);

    public string? GetUpdate()
        => _buffer.BuildUpdateXml(_channelId, _sentIds);
}
