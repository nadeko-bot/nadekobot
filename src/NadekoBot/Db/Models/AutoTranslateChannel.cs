#nullable disable
namespace NadekoBot.Db.Models;

public class AutoTranslateChannel : DbEntity
{
    public ulong GuildId { get; set; }
    public ulong ChannelId { get; set; }
    public bool AutoDelete { get; set; }
    public IList<AutoTranslateUser> Users { get; set; } = new List<AutoTranslateUser>();
}