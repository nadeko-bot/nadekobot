#nullable disable
namespace NadekoBot.Db.Models;

public class WarnTemplate : DbEntity
{
    public ulong GuildId { get; set; }
    public string Text { get; set; } = null!;
}
