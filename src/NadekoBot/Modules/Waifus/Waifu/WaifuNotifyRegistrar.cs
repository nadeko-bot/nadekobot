using NadekoBot.Modules.Utility.UserNotifications;

namespace NadekoBot.Modules.Waifus;

public sealed class WaifuNotifyRegistrar : INService, IUserNotifyEventRegistrar
{
    public IReadOnlyList<UserNotifyEventInfo> GetEvents() =>
    [
        new("waifu.manager_replaced", strs.notify_waifu_manager_replaced),
        new("waifu.new_manager", strs.notify_waifu_new_manager),
    ];
}
