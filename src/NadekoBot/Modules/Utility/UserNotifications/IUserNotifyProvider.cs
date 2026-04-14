namespace NadekoBot.Modules.Utility.UserNotifications;

public interface IUserNotifyEventRegistrar
{
    IReadOnlyList<UserNotifyEventInfo> GetEvents();
}

public readonly record struct UserNotifyEventInfo(
    string Key,
    LocStr Name);
