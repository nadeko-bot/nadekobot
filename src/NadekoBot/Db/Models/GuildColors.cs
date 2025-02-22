using System.ComponentModel.DataAnnotations;

namespace NadekoBot.Db.Models;

public class GuildColors
{
    [Key]
    public int Id { get; set; }
    public ulong GuildId { get; set; }

    [MaxLength(9)]
    public string? OkColor { get; set; }

    [MaxLength(9)]
    public string? ErrorColor { get; set; }

    [MaxLength(9)]
    public string? PendingColor { get; set; }
}