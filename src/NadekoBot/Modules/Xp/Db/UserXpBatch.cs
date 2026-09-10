#nullable disable warnings
using System.ComponentModel.DataAnnotations;

namespace NadekoBot.Modules.Xp.Services;

public sealed class UserXpBatch
{
    [Key]
    public ulong UserId { get; set; }

    public ulong GuildId { get; set; }
    public long XpToGain { get; set; } = 0;
    public bool CountsForClub { get; set; }
}