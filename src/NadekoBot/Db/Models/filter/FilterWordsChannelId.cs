#nullable disable
namespace NadekoBot.Db.Models;

public class FilterWordsChannelId : DbEntity
{
    public int? GuildFilterConfigId { get; set; }
    public ulong ChannelId { get; set; }

    protected bool Equals(FilterWordsChannelId other)
        => ChannelId == other.ChannelId;

    public override bool Equals(object obj)
        => obj is FilterWordsChannelId other && Equals(other);

    public override int GetHashCode()
        => ChannelId.GetHashCode();
}