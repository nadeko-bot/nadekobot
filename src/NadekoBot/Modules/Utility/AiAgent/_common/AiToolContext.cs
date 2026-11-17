namespace NadekoBot.Modules.Utility.AiAgent;

// Every tool checks this before it acts, so the agent can never exceed the permissions of the user.
public sealed class AiToolContext
{
    public required IGuild Guild { get; init; }

    public required ITextChannel SourceChannel { get; init; }

    public required IGuildUser User { get; init; }

    public required IUserMessage TriggerMessage { get; init; }

    public required CancellationToken CancellationToken { get; init; }

    // Set by close_session, to stop the session from opening again after the agent responds.
    public bool SessionClosed { get; set; }

    public bool AskPending { get; set; }

    // Null when the bot opens no session after this request.
    public MessageComponent? EndSessionComponent { get; init; }

    // The ask_user question which has the End session button, 0 when it has none.
    public ulong QuestionButtonId { get; set; }

    // Messages which send_message sent for the member in this conversation. The member may edit or delete them without Manage Messages.
    public ConcurrentDictionary<ulong, bool> SentMessageIds { get; } = new();

    // The member's own words of this request, without the context which the bot adds and without custom emoji.
    public string RequestText
    {
        get => _requestText;
        init => _requestText = GuardText.WithoutCustomEmoji(value);
    }

    private readonly string _requestText = "";
    private string? _normalizedRequest;

    public string NormalizedRequest
        => _normalizedRequest ??= GuardText.Normalize(RequestText);

    // The request which the agent's last question was about, when this request answers it.
    public string? EarlierRequest { get; init; }

    // Null in tests which run one request.
    public AgentConversation? Conversation { get; init; }

    public string? Question { get; set; }

    public AgentSessionState Session { get; } = new();

    private long _lastOutputId;

    // The newest message which a tool posted in the source channel. The answer must not go above it.
    public ulong LastOutputId
        => (ulong)Interlocked.Read(ref _lastOutputId);

    public void NoteOutput(ulong channelId, ulong messageId)
    {
        if (channelId != SourceChannel.Id)
            return;

        var id = (long)messageId;
        var last = Interlocked.Read(ref _lastOutputId);
        while (id > last)
        {
            var seen = Interlocked.CompareExchange(ref _lastOutputId, id, last);
            if (seen == last)
                return;

            last = seen;
        }
    }
}
