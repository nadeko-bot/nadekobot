namespace NadekoBot.Db.Models;

[ShardFiltered]
public class AutoPublishChannel : DbEntity
{
    public ulong GuildId { get; set; }
    public ulong ChannelId { get; set; }
}