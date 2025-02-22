#nullable disable
using System.ComponentModel.DataAnnotations;

namespace NadekoBot.Db.Models;

public class ClubInfo : DbEntity
{
    [MaxLength(20)]
    public string Name { get; set; }
    public string Description { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    
    public int Xp { get; set; } = 0;
    public int? OwnerId { get; set; }
    public DiscordUser Owner { get; set; }

    public List<DiscordUser> Members { get; set; } = new();
    public List<ClubApplicants> Applicants { get; set; } = new();
    public List<ClubBans> Bans { get; set; } = new();

    public override string ToString()
        => Name;
}

public class ClubApplicants
{
    public int ClubId { get; set; }
    public ClubInfo Club { get; set; }

    public int UserId { get; set; }
    public DiscordUser User { get; set; }
}

public class ClubBans
{
    public int ClubId { get; set; }
    public ClubInfo Club { get; set; }

    public int UserId { get; set; }
    public DiscordUser User { get; set; }
}