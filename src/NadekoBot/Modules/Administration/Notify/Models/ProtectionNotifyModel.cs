#nullable disable
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Administration.Services;

public record struct ProtectionNotifyModel(ulong GuildId, ProtectionType ProtType, ulong UserId) : INotifyModel<ProtectionNotifyModel>
{
    public static string KeyName
        => "notify.protection";

    public static NotifyType NotifyType
        => NotifyType.Protection;

    public const string PH_TYPE = "type";

    public static IReadOnlyList<NotifyModelPlaceholderData<ProtectionNotifyModel>> GetReplacements()
    {
        return [
            new(PH_TYPE, static (data, g) => data.ProtType.ToString() )
        ];
    }

    public bool TryGetUserId(out ulong userId)
    {
        userId = UserId;
        return true;
    }

    public bool TryGetGuildId(out ulong guildId)
    {
        guildId = GuildId;
        return true;
    }
}