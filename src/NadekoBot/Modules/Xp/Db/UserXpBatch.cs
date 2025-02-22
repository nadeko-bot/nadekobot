#nullable disable warnings
using System.ComponentModel.DataAnnotations;

namespace NadekoBot.Modules.Xp.Services;

public sealed class UserXpBatch
{
    [Key]
    public ulong UserId { get; set; }

    public ulong GuildId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string AvatarId { get; set; } = string.Empty;
}