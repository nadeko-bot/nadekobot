#nullable disable
namespace NadekoBot.Db.Models;

public class WarningPunishment : DbEntity
{
    public ulong GuildId { get; set; }
    public int Count { get; set; }
    public PunishmentAction Punishment { get; set; }
    public int Time { get; set; }
    public ulong? RoleId { get; set; }
}