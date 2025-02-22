#nullable disable
namespace NadekoBot.Db.Models;

public class RewardedUser : DbEntity
{
    public ulong UserId { get; set; }
    public string PlatformUserId { get; set; }
    public long AmountRewardedThisMonth { get; set; }
    public DateTime LastReward { get; set; }
}