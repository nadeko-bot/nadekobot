#nullable disable
namespace NadekoBot.Db.Models;

public class Warning : DbEntity
{
    public ulong GuildId { get; set; }
    public ulong UserId { get; set; }
    public string Reason { get; set; }
    public bool Forgiven { get; set; }
    public string ForgivenBy { get; set; }
    public string Moderator { get; set; }
    public long Weight { get; set; }
}