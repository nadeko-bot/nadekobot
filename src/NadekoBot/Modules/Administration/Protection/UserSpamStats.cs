#nullable disable
namespace NadekoBot.Modules.Administration;

public sealed class UserSpamStats
{
    public int Count
    {
        get
        {
            lock (_applyLock)
            {
                Cleanup();
                return _messageTracker.Count;
            }
        }
    }

    private string lastMessage;

    private readonly Queue<DateTime> _messageTracker;

    private readonly object _applyLock = new();

    private readonly TimeSpan _maxTime = TimeSpan.FromMinutes(30);

    public UserSpamStats(IUserMessage msg)
    {
        lastMessage = msg.Content.ToUpperInvariant();
        _messageTracker = new();

        ApplyNextMessage(msg);
    }

    public void ApplyNextMessage(IUserMessage message)
    {
        var upperMsg = message.Content.ToUpperInvariant();

        lock (_applyLock)
        {
            if (upperMsg != lastMessage || (string.IsNullOrWhiteSpace(upperMsg) && message.Attachments.Any()))
            {
                // if it's a new message, reset spam counter
                lastMessage = upperMsg;
                _messageTracker.Clear();
            }

            _messageTracker.Enqueue(DateTime.UtcNow);
        }
    }

    private void Cleanup()
    {
        lock (_applyLock)
        {
            while (_messageTracker.TryPeek(out var dateTime))
            {
                if (DateTime.UtcNow - dateTime < _maxTime)
                    break;

                _messageTracker.Dequeue();
            }
        }
    }
}