using NadekoBot.Db.Models;
using NadekoBot.Modules.Administration;

namespace NadekoBot.Modules.Xp.Services;

public record struct RemoveRoleRewardNotifyModel(ulong GuildId, ulong RoleId, ulong UserId, long Level) : INotifyModel<RemoveRoleRewardNotifyModel>
{
    public static string KeyName
        => "notify.reward.removerole";

    public static NotifyType NotifyType
        => NotifyType.RemoveRoleReward;

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

    public const string PH_USER = "user";
    public const string PH_ROLE = "role";
    public const string PH_LEVEL = "level";

    public static IReadOnlyList<NotifyModelPlaceholderData<RemoveRoleRewardNotifyModel>> GetReplacements()
    {
        return [
            new(PH_USER, static (data, g) => g.GetUser(data.UserId)?.ToString() ?? data.UserId.ToString() ),
            new(PH_ROLE, static (data, g) => g.GetRole(data.RoleId)?.ToString() ?? data.RoleId.ToString() ),
            new(PH_LEVEL, static (data, g) => data.Level.ToString() ),
        ];
    }
}