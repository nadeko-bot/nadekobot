using System.ComponentModel.DataAnnotations;

namespace NadekoBot.Modules.Games;

public sealed class UserFishStats
{
    [Key]
    public int Id { get; set; }

    public ulong UserId { get; set; }
    public int Skill { get; set; }
}