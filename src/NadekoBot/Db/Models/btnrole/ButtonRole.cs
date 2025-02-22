using System.ComponentModel.DataAnnotations;

namespace NadekoBot.Db.Models;

public sealed class ButtonRole
{
    [Key]
    public int Id { get; set; }

    [MaxLength(200)]
    public string ButtonId { get; set; } = string.Empty;

    public ulong GuildId { get; set; }
    public ulong ChannelId { get; set; }
    public ulong MessageId { get; set; }

    public int Position { get; set; }
    public ulong RoleId { get; set; }

    [MaxLength(100)]
    public string Emote { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Label { get; set; } = string.Empty;

    public bool Exclusive { get; set; }
}