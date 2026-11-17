namespace NadekoBot.Modules.Utility.AiAgent;

// Later steps get only the new messages, so the prompt cache holds; one session uses it one step at a time, so no lock.
public sealed class ChannelHistoryFeed
{
    private readonly ChannelMessageBuffer _buffer;
    private readonly ulong _channelId;
    private readonly string _channelName;
    private readonly HashSet<ulong> _sentIds;
    private readonly ulong _memberId;

    public ChannelHistoryFeed(ChannelMessageBuffer buffer, ulong channelId, string channelName, ulong triggerMessageId, ulong memberId = 0)
    {
        _buffer = buffer;
        _channelId = channelId;
        _channelName = channelName;
        _sentIds = [triggerMessageId];
        _memberId = memberId;
    }

    // Contents gets the text of each message for the untrusted text check, but not the member's own, which the member chose.
    public string? GetSnapshot(List<string>? contents = null)
        => _buffer.BuildHistoryXml(_channelId, _channelName, _sentIds, contents, _memberId);

    public string? GetUpdate(List<string>? contents = null)
        => _buffer.BuildUpdateXml(_channelId, _sentIds, contents, _memberId);
}
