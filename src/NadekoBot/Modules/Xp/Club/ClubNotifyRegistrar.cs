using NadekoBot.Modules.Utility.UserNotifications;

namespace NadekoBot.Modules.Xp.Services;

public sealed class ClubNotifyRegistrar : INService, IUserNotifyEventRegistrar
{
    public IReadOnlyList<UserNotifyEventInfo> GetEvents() =>
    [
        new("club.application_accepted", strs.notify_club_app_accepted),
    ];
}
