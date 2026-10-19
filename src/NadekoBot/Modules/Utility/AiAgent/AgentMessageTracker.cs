namespace NadekoBot.Modules.Utility.AiAgent;

// A reply starts the agent only on these messages, so other bot output (feeds, games, command results) never does.
public sealed class AgentMessageTracker : INService
{
    public const int MAX_TRACKED = 20_000;
    public static readonly TimeSpan TrackDuration = TimeSpan.FromHours(24);

    private readonly ConcurrentDictionary<ulong, bool> _messages = new();
    private int _fullWarned;

    public void Add(ulong messageId)
    {
        if (_messages.Count >= MAX_TRACKED)
        {
            if (Interlocked.Exchange(ref _fullWarned, 1) == 0)
                Log.Warning("AI agent message tracker is full ({Max} ids). Replies to new agent messages will not start the agent",
                    MAX_TRACKED);
            return;
        }

        _messages.TryAdd(messageId, true);
    }

    public bool IsPingedReplyToAgent(IUserMessage msg, ulong botId)
    {
        if (msg.ReferencedMessage is not { } reply || reply.Author?.Id != botId)
            return false;

        if (!IsBotMentioned(msg, botId))
            return false;

        return _messages.ContainsKey(reply.Id);
    }

    // Discord lists the author of the replied message as mentioned only when the reply ping is on.
    private static bool IsBotMentioned(IUserMessage msg, ulong botId)
    {
        foreach (var id in msg.MentionedUserIds)
        {
            if (id == botId)
                return true;
        }

        return false;
    }

    // The age comes from the snowflake id itself, so no timestamp is stored.
    public void CleanExpired(DateTimeOffset now)
    {
        var cutoff = SnowflakeUtils.ToSnowflake(now - TrackDuration);
        foreach (var (id, _) in _messages)
        {
            if (id < cutoff)
                _messages.TryRemove(id, out _);
        }

        Volatile.Write(ref _fullWarned, 0);
    }
}
