using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Owner.Services;

public interface IBotActivityService
{
    BotPresenceState GetPresence();
    Task<PresenceError> SetPresenceAsync(UserStatus status, ActivityType? type, string? text, string? url);
    Task<PresenceError> SetStatusAsync(UserStatus status);
    Task<PresenceError> SetActivityAsync(ActivityType? type, string? text);
    Task<PresenceError> SetStreamAsync(string url, string name);
    Task ApplySavedAsync();
    Task<bool> ToggleRotatePlayingAsync();
    Task AddPlaying(ActivityType statusType, string status);
    Task<string?> RemovePlayingAsync(int index);
    IReadOnlyList<RotatingPlayingStatus> GetRotatingStatuses();
}
