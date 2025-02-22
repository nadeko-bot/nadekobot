using System.ComponentModel.DataAnnotations;

namespace NadekoBot.Db.Models;

public class HoneypotChannel
{
    [Key]
    public ulong GuildId { get; set; }
    
    public ulong ChannelId { get; set; }
}