#nullable disable
namespace NadekoBot.Db.Models;

[ShardFiltered]
public class FlagTranslateChannel : DbEntity
{
    public ulong GuildId { get; set; }
    public ulong ChannelId { get; set; }
}