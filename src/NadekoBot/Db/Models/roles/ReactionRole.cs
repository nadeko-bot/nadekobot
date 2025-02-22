#nullable disable
using System.ComponentModel.DataAnnotations;

namespace NadekoBot.Db.Models;

public class ReactionRoleV2 : DbEntity
{
    public ulong GuildId { get; set; }
    public ulong ChannelId { get; set; }
    
    public ulong MessageId { get; set; }
    
    [MaxLength(100)]
    public string Emote { get; set; }
    public ulong RoleId { get; set; }
    public int Group { get; set; }
    public int LevelReq { get; set; }
}