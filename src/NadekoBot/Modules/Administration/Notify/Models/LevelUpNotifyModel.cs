using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Administration;

public readonly record struct LevelUpNotifyModel(
    ulong GuildId,
    ulong ChannelId,
    ulong UserId,
    long Level) : INotifyModel<LevelUpNotifyModel>
{
    public static string KeyName 
        => "notify.levelup";

    public static NotifyType NotifyType
        => NotifyType.LevelUp;

    public const string PH_USER = "user";
    public const string PH_LEVEL = "level";

    public static IReadOnlyList<NotifyModelPlaceholderData<LevelUpNotifyModel>> GetReplacements()
    {
        return
        [
            new(PH_LEVEL, static (data, g) => data.Level.ToString() ),
            new(PH_USER, static (data, g) => g.GetUser(data.UserId)?.ToString() ?? data.UserId.ToString() )
        ];
    }

    public readonly bool TryGetGuildId(out ulong guildId)
    {
        guildId = GuildId;
        return true;
    }

    public readonly bool TryGetUserId(out ulong userId)
    {
        userId = UserId;
        return true;
    }
}
