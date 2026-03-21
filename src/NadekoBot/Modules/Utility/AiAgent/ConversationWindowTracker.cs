namespace NadekoBot.Modules.Utility.AiAgent;

/// <summary>
/// Tracks active conversation windows between users and the bot.
/// Shared between AiAgentService (writes) and CloseSessionTool (reads/removes).
/// </summary>
public sealed class ConversationWindowTracker : INService
{
    private readonly ConcurrentDictionary<(ulong UserId, ulong ChannelId), DateTime> _windows = new();

    /// <summary>
    /// Opens or refreshes a conversation window for a user+channel pair
    /// </summary>
    public void Open(ulong userId, ulong channelId)
        => _windows[(userId, channelId)] = DateTime.UtcNow;

    /// <summary>
    /// Checks if a conversation window is active for this user+channel
    /// </summary>
    public bool IsActive(ulong userId, ulong channelId, double windowSeconds)
    {
        if (!_windows.TryGetValue((userId, channelId), out var lastResponse))
            return false;

        if ((DateTime.UtcNow - lastResponse).TotalSeconds <= windowSeconds)
            return true;

        _windows.TryRemove((userId, channelId), out _);
        return false;
    }

    /// <summary>
    /// Closes a conversation window. Returns true if one was active.
    /// </summary>
    public bool Close(ulong userId, ulong channelId)
        => _windows.TryRemove((userId, channelId), out _);

    /// <summary>
    /// Closes all conversation windows for a user across all channels
    /// </summary>
    public void CloseAll(ulong userId)
    {
        foreach (var key in _windows.Keys)
        {
            if (key.UserId == userId)
                _windows.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Removes all expired windows
    /// </summary>
    public void CleanExpired(double windowSeconds)
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-windowSeconds);
        foreach (var (key, timestamp) in _windows)
        {
            if (timestamp < cutoff)
                _windows.TryRemove(key, out _);
        }
    }
}
