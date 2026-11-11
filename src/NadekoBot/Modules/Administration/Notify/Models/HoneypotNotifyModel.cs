using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Administration;

public readonly record struct HoneypotNotifyModel(
    ulong GuildId,
    ulong UserId,
    ulong HoneypotChannelId,
    HoneypotAction Action
) : INotifyModel<HoneypotNotifyModel>
{
    public const string PH_CHANNEL = "channel";
    public const string PH_ACTION = "action";

    public static string KeyName
        => "notify.honeypot";

    public static NotifyType NotifyType
        => NotifyType.Honeypot;

    public static IReadOnlyList<NotifyModelPlaceholderData<HoneypotNotifyModel>> GetReplacements()
        =>
        [
            new(PH_CHANNEL, static (data, _) => $"<#{data.HoneypotChannelId}>"),
            new(PH_ACTION, static (data, _) => data.Action.ToString()),
        ];

    public bool TryGetGuildId(out ulong guildId)
    {
        guildId = GuildId;
        return true;
    }

    public bool TryGetUserId(out ulong userId)
    {
        userId = UserId;
        return true;
    }
}
