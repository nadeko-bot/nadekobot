using System.ComponentModel.DataAnnotations;

namespace NadekoBot.Db.Models;

public class Notify
{
    [Key]
    public int Id { get; set; }

    public ulong GuildId { get; set; }
    public ulong ChannelId { get; set; }
    public NotifyType Type { get; set; }

    [MaxLength(10_000)]
    public string Message { get; set; } = string.Empty;
}

public enum NotifyType
{
    LevelUp = 0,
    Protection = 1, Prot = 1,
    AddRoleReward = 2,
    RemoveRoleReward = 3,
}