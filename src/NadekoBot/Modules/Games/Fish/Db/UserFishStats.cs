using System.ComponentModel.DataAnnotations;

namespace NadekoBot.Modules.Games;

public sealed class UserFishStats
{
    [Key]
    public int Id { get; set; }

    public ulong UserId { get; set; }
    public int Skill { get; set; }

    public int? Pole { get; set; }
    public int? Bait { get; set; }
}

// public sealed class FishingPole
// {
    // [Key]
    // public int Id { get; set; }

    // public string Name { get; set; } = string.Empty;

    // public long Price { get; set; }

    // public string Emoji { get; set; } = string.Empty;


// }