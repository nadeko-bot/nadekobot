#nullable disable
namespace NadekoBot.Db.Models;

public class UserXpStats : DbEntity
{
    public ulong UserId { get; set; }
    public ulong GuildId { get; set; }
    public long Xp { get; set; }
}