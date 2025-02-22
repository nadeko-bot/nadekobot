using NadekoBot.Db.Models;
using NadekoBot.Modules.Administration;

namespace NadekoBot.Modules.Xp.Services;

public record struct AddRoleRewardNotifyModel(ulong GuildId, ulong RoleId, ulong UserId, long Level) : INotifyModel<AddRoleRewardNotifyModel>
{
    public static string KeyName
        => "notify.reward.addrole";

    public static NotifyType NotifyType
        => NotifyType.AddRoleReward;

    public const string PH_LEVEL = "level";
    public const string PH_USER = "user";
    public const string PH_ROLE = "role";

    public static IReadOnlyList<NotifyModelPlaceholderData<AddRoleRewardNotifyModel>> GetReplacements()
        => 
        [
            new(PH_LEVEL, static (data, g) => data.Level.ToString() ),
            new(PH_USER, static (data, g) => g.GetUser(data.UserId)?.ToString() ?? data.UserId.ToString() ),
            new(PH_ROLE, static (data, g) => g.GetRole(data.RoleId)?.ToString() ?? data.RoleId.ToString() )
        ];

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