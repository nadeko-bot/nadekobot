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

    private string lastFingerprint;

    private readonly Queue<DateTime> _messageTracker;

    private readonly Lock _applyLock = new();

    private readonly TimeSpan _maxTime = TimeSpan.FromMinutes(30);

    public UserSpamStats(IUserMessage msg)
    {
        lastFingerprint = GetFingerprint(msg);
        _messageTracker = new();

        ApplyNextMessage(msg);
    }

    public void ApplyNextMessage(IUserMessage message)
    {
        var fingerprint = GetFingerprint(message);

        lock (_applyLock)
        {
            if (fingerprint != lastFingerprint)
            {
                lastFingerprint = fingerprint;
                _messageTracker.Clear();
            }

            _messageTracker.Enqueue(DateTime.UtcNow);
        }
    }

    /// <summary>
    /// Generates a fingerprint for a message based on its text content and attachment metadata.
    /// </summary>
    private static string GetFingerprint(IUserMessage message)
    {
        var upperContent = message.Content.ToUpperInvariant();

        if (!message.Attachments.Any())
            return upperContent;

        var attachPart = string.Join(',', message.Attachments
            .OrderBy(a => a.Filename)
            .Select(a => $"{a.Filename}:{a.Size}"));

        if (string.IsNullOrWhiteSpace(upperContent))
            return $"\x01ATTACH:{attachPart}";

        return $"{upperContent}\x01ATTACH:{attachPart}";
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