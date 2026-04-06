using System.ComponentModel.DataAnnotations;

namespace NadekoBot.Db.Models;

public enum HoneypotAction
{
    Softban = 0,
    Ban = 1,
}

public class HoneypotChannel
{
    [Key]
    public ulong GuildId { get; set; }

    public ulong ChannelId { get; set; }

    public HoneypotAction Action { get; set; }
}