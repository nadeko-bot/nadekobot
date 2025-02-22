#nullable disable
namespace NadekoBot.Db.Models;

public class WaifuUpdate : DbEntity
{
    public int UserId { get; set; }
    public DiscordUser User { get; set; }
    public WaifuUpdateType UpdateType { get; set; }

    public int? OldId { get; set; }
    public DiscordUser Old { get; set; }

    public int? NewId { get; set; }
    public DiscordUser New { get; set; }
}