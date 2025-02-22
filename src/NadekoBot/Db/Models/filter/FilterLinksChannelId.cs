namespace NadekoBot.Db.Models;

public class FilterLinksChannelId : DbEntity
{
    public ulong ChannelId { get; set; }
    public int? GuildFilterConfigId { get; set; }

    protected bool Equals(FilterLinksChannelId other)
        => ChannelId == other.ChannelId;

    public override bool Equals(object? obj)
        => obj is FilterLinksChannelId other && Equals(other);

    public override int GetHashCode()
        => ChannelId.GetHashCode();
}