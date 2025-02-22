using System.ComponentModel.DataAnnotations;

namespace NadekoBot.Db.Models;

public sealed class SarGroup
{
    [Key]
    public int Id { get; set; }

    public int GroupNumber { get; set; }
    public ulong GuildId { get; set; }
    public ulong? RoleReq { get; set; }
    public ICollection<Sar> Roles { get; set; } = [];
    public bool IsExclusive { get; set; }
    
    [MaxLength(100)]
    public string? Name { get; set; }
}