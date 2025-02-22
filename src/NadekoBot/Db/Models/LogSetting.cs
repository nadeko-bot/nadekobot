#nullable disable
namespace NadekoBot.Db.Models;

public class LogSetting : DbEntity
{
    public List<IgnoredLogItem> LogIgnores { get; set; } = new();
    
    public ulong GuildId { get; set; }
    public ulong? LogOtherId { get; set; }
    public ulong? MessageUpdatedId { get; set; }
    public ulong? MessageDeletedId { get; set; }

    public ulong? UserJoinedId { get; set; }
    public ulong? UserLeftId { get; set; }
    public ulong? UserBannedId { get; set; }
    public ulong? UserUnbannedId { get; set; }
    public ulong? UserUpdatedId { get; set; }

    public ulong? ChannelCreatedId { get; set; }
    public ulong? ChannelDestroyedId { get; set; }
    public ulong? ChannelUpdatedId { get; set; }
    
    
    public ulong? ThreadDeletedId { get; set; }
    public ulong? ThreadCreatedId { get; set; }

    public ulong? UserMutedId { get; set; }

    //userpresence
    public ulong? LogUserPresenceId { get; set; }

    //voicepresence

    public ulong? LogVoicePresenceId { get; set; }
    
    public ulong? LogVoicePresenceTTSId { get; set; }
    public ulong? LogWarnsId { get; set; }
}