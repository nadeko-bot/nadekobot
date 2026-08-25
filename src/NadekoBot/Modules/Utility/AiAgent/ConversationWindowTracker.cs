namespace NadekoBot.Modules.Utility.AiAgent;

// Written by AiAgentService, read and removed by CloseSessionTool.
public sealed class ConversationWindowTracker : INService
{
    private readonly ConcurrentDictionary<(ulong UserId, ulong ChannelId), DateTime> _windows = new();

    public void Open(ulong userId, ulong channelId)
        => _windows[(userId, channelId)] = DateTime.UtcNow;

    public bool IsActive(ulong userId, ulong channelId, double windowSeconds)
    {
        if (!_windows.TryGetValue((userId, channelId), out var lastResponse))
            return false;

        if ((DateTime.UtcNow - lastResponse).TotalSeconds <= windowSeconds)
            return true;

        _windows.TryRemove((userId, channelId), out _);
        return false;
    }

    public bool Close(ulong userId, ulong channelId)
        => _windows.TryRemove((userId, channelId), out _);

    public void CloseAll(ulong userId)
    {
        foreach (var key in _windows.Keys)
        {
            if (key.UserId == userId)
                _windows.TryRemove(key, out _);
        }
    }

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
