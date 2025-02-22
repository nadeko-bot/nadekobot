#nullable disable
namespace NadekoBot.Db.Models;

public class FlagTranslateChannel : DbEntity
{
    public ulong GuildId { get; set; }
    public ulong ChannelId { get; set; }
}