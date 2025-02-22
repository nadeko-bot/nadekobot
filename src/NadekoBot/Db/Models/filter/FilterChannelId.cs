using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NadekoBot.Db.Models;

public class FilterChannelId
{
    [Key]
    public int Id { get; set; }
    
    public int? GuildFilterConfigId { get; set; }

    public ulong ChannelId { get; set; }

    protected bool Equals(FilterChannelId other)
        => ChannelId == other.ChannelId;

    public override bool Equals(object? obj)
        => obj is FilterChannelId fci && fci.Equals(this);

    public override int GetHashCode()
        => ChannelId.GetHashCode();
}