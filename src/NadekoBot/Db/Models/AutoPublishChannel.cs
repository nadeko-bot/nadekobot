namespace NadekoBot.Db.Models;

public class AutoPublishChannel : DbEntity
{
    public ulong GuildId { get; set; }
    public ulong ChannelId { get; set; }
}