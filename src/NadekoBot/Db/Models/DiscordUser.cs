namespace NadekoBot.Db.Models;


// FUTURE remove LastLevelUp from here and UserXpStats
public class DiscordUser : DbEntity
{
    public const string DEFAULT_USERNAME = "??Unknown";
    
    public ulong UserId { get; set; }
    public string? Username { get; set; }
    public string? AvatarId { get; set; }

    public int? ClubId { get; set; }
    public ClubInfo? Club { get; set; }
    public bool IsClubAdmin { get; set; }

    public long TotalXp { get; set; }
    public XpNotificationLocation NotifyOnLevelUp { get; set; }

    public long CurrencyAmount { get; set; }

    public override bool Equals(object? obj)
        => obj is DiscordUser du ? du.UserId == UserId : false;

    public override int GetHashCode()
        => UserId.GetHashCode();

    public override string ToString()
        => Username ?? DEFAULT_USERNAME;
}