#nullable disable
namespace NadekoBot.Db.Models;

public class BlacklistEntry : DbEntity
{
    public ulong ItemId { get; set; }
    public BlacklistType Type { get; set; }
}

public enum BlacklistType
{
    Server,
    Channel,
    User
}