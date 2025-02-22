#nullable disable
namespace NadekoBot.Db.Models;

public class StreamOnlineMessage : DbEntity
{
    public ulong ChannelId { get; set; }
    public ulong MessageId { get; set; }

    public FollowedStream.FType Type { get; set; }
    public string Name { get; set; }
}