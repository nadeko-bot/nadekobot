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

    private ulong _lastFingerprint;

    private readonly Queue<DateTime> _messageTracker;

    private readonly Lock _applyLock = new();

    private readonly TimeSpan _maxTime = TimeSpan.FromMinutes(30);

    public UserSpamStats(IUserMessage msg)
    {
        _lastFingerprint = GetFingerprint(msg);
        _messageTracker = new();

        ApplyNextMessage(msg);
    }

    public void ApplyNextMessage(IUserMessage message)
    {
        var fingerprint = GetFingerprint(message);

        lock (_applyLock)
        {
            if (fingerprint != _lastFingerprint)
            {
                _lastFingerprint = fingerprint;
                _messageTracker.Clear();
            }

            _messageTracker.Enqueue(DateTime.UtcNow);
        }
    }

    private static ulong GetFingerprint(IUserMessage message)
    {
        var contentHash = (ulong)(uint)string.GetHashCode(
            message.Content.AsSpan(), StringComparison.InvariantCultureIgnoreCase);

        if (message.Attachments.Count == 0)
            return contentHash;

        ulong attachHash = 0;
        foreach (var a in message.Attachments)
        {
            var nameHash = (uint)string.GetHashCode(
                a.Filename.AsSpan(), StringComparison.InvariantCultureIgnoreCase);
            var sizeBits = (uint)a.Size;
            attachHash ^= ((ulong)nameHash << 32) | sizeBits;
        }

        return (contentHash << 1) ^ attachHash;
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