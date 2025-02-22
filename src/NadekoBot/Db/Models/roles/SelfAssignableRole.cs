using System.ComponentModel.DataAnnotations;

namespace NadekoBot.Db.Models;

public sealed class Sar
{
    [Key]
    public int Id { get; set; }

    public ulong GuildId { get; set; }
    public ulong RoleId { get; set; }

    public int SarGroupId { get; set; }
    public int LevelReq { get; set; }
}

public sealed class SarAutoDelete
{
    [Key]
    public int Id { get; set; }

    public ulong GuildId { get; set; }
    public bool IsEnabled { get; set; } = false;
}