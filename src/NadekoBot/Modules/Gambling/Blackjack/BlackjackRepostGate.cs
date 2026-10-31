namespace NadekoBot.Modules.Gambling.Blackjack;

// Counts the messages which came after the table message. The gateway thread counts, the table loop reads and resets.
public sealed class BlackjackRepostGate(TimeProvider time)
{
    public const int MESSAGES_BEFORE_REPOST = 10;
    public static readonly TimeSpan MinRepostInterval = TimeSpan.FromSeconds(15);

    private ulong _messageId;
    private int _otherMessages;
    private DateTimeOffset _postedAt;

    public bool IsDue
        => Volatile.Read(ref _otherMessages) >= MESSAGES_BEFORE_REPOST
           && time.GetUtcNow() >= _postedAt + MinRepostInterval;

    public void OnMessage(ulong messageId)
    {
        if (messageId != Volatile.Read(ref _messageId))
            Interlocked.Increment(ref _otherMessages);
    }

    // The gateway can report the new table message before the send returns; the reset after the id clears that count.
    public void OnTablePosted(ulong messageId)
    {
        Volatile.Write(ref _messageId, messageId);
        Interlocked.Exchange(ref _otherMessages, 0);
        _postedAt = time.GetUtcNow();
    }
}
