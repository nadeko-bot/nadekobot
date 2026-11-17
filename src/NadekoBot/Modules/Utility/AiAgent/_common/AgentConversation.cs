namespace NadekoBot.Modules.Utility.AiAgent;

// Takes a lock, because after .agentcancel two requests can overlap.
public sealed class AgentConversation
{
    public const int MAX_MEMBER_TEXTS = 5;
    public const int MAX_SENT_MESSAGES = 20;
    public const int MAX_REQUEST_CHARS = 2_000;

    private readonly Lock _lock = new();
    private readonly Queue<string> _memberTexts = new();
    private readonly List<ulong> _sentMessages = [];
    private AgentQuestion? _question;

    public AgentConversation(long now)
        => LastActive = now;

    // Environment.TickCount64 at the end of the last request.
    public long LastActive { get; set; }

    public string[] GetMemberTexts()
    {
        lock (_lock)
            return _memberTexts.ToArray();
    }

    public AgentQuestion? TakeQuestion()
    {
        lock (_lock)
        {
            var question = _question;
            _question = null;
            return question;
        }
    }

    // A request which answered the question but failed gives it back, so the member can answer again.
    public void RestoreQuestion(AgentQuestion question)
    {
        lock (_lock)
            _question ??= question;
    }

    public bool ClearQuestion()
    {
        lock (_lock)
        {
            var had = _question is not null;
            _question = null;
            return had;
        }
    }

    // Before a request: the member may edit or delete what the agent sent for them in this conversation.
    public void Begin(AiToolContext context)
    {
        lock (_lock)
        {
            foreach (var id in _sentMessages)
                context.SentMessageIds[id] = true;

            AddMemberTextInternal(context.NormalizedRequest);
        }
    }

    // A cancelled request leaves no question, because the member stopped it.
    public void End(AiToolContext context)
    {
        lock (_lock)
        {
            foreach (var (id, _) in context.SentMessageIds)
                AddSentMessageInternal(id);

            if (context.AskPending && !context.CancellationToken.IsCancellationRequested)
                _question = new(context.RequestText, context.Question ?? "");

            LastActive = Environment.TickCount64;
        }
    }

    // The answer alone ("#general") means nothing without the question; the model wrote the question, so it is sanitized.
    public static string PromptFor(string request, AgentQuestion? question)
        => question is null
            ? request
            : "[The member's earlier request: " + question.Request + "]\n[Your question: " + PromptSanitizer.Sanitize(question.Text)
              + "]\nThe member's answer: " + request;

    private void AddMemberTextInternal(string normalized)
    {
        // Each conversation keeps these texts, so a long merged queue keeps only its start.
        if (normalized.Length > MAX_REQUEST_CHARS)
            normalized = normalized[..(char.IsHighSurrogate(normalized[MAX_REQUEST_CHARS - 1]) ? MAX_REQUEST_CHARS - 1 : MAX_REQUEST_CHARS)];

        _memberTexts.Enqueue(normalized);
        while (_memberTexts.Count > MAX_MEMBER_TEXTS)
            _memberTexts.Dequeue();
    }

    private void AddSentMessageInternal(ulong id)
    {
        if (_sentMessages.Contains(id))
            return;

        _sentMessages.Add(id);
        if (_sentMessages.Count > MAX_SENT_MESSAGES)
            _sentMessages.RemoveAt(0);
    }
}

public sealed record AgentQuestion(string Request, string Text);

public sealed class AgentConversationStore : INService
{
    public const int MAX_CONVERSATIONS = 2_000;

    private static readonly TimeSpan _minKeep = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<(ulong UserId, ulong ChannelId), AgentConversation> _conversations = new();

    public static TimeSpan Keep(AiAgentConfig config)
    {
        var window = TimeSpan.FromSeconds(Math.Max(0, config.FollowUpWindowSeconds));
        return window > _minKeep ? window : _minKeep;
    }

    public AgentConversation Get(ulong userId, ulong channelId, TimeSpan keep)
    {
        var key = (userId, channelId);
        var now = Environment.TickCount64;
        if (_conversations.TryGetValue(key, out var conv) && !IsExpiredInternal(conv, now, keep))
        {
            // A long request must not expire while it runs.
            conv.LastActive = now;
            return conv;
        }

        if (_conversations.Count >= MAX_CONVERSATIONS)
            MakeRoomInternal(keep);

        conv = new(now);
        _conversations[key] = conv;
        return conv;
    }

    // .agentcancel also drops the open questions of the member, so the next message is no answer.
    public bool ClearQuestions(ulong userId)
    {
        var cleared = false;
        foreach (var (key, conv) in _conversations)
        {
            if (key.UserId == userId)
                cleared |= conv.ClearQuestion();
        }

        return cleared;
    }

    public void RemoveExpired(TimeSpan keep)
    {
        var now = Environment.TickCount64;
        foreach (var (key, conv) in _conversations)
        {
            if (IsExpiredInternal(conv, now, keep))
                _conversations.TryRemove(new KeyValuePair<(ulong, ulong), AgentConversation>(key, conv));
        }
    }

    private static bool IsExpiredInternal(AgentConversation conv, long now, TimeSpan keep)
        => now - conv.LastActive > (long)keep.TotalMilliseconds;

    private void MakeRoomInternal(TimeSpan keep)
    {
        RemoveExpired(keep);
        if (_conversations.Count < MAX_CONVERSATIONS)
            return;

        (ulong, ulong)? oldest = null;
        var oldestTime = long.MaxValue;
        foreach (var (key, conv) in _conversations)
        {
            if (conv.LastActive < oldestTime)
            {
                oldest = key;
                oldestTime = conv.LastActive;
            }
        }

        if (oldest is { } k)
            _conversations.TryRemove(k, out _);
    }
}
