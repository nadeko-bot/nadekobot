using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NadekoBot.Common.TypeReaders.Models;
using NadekoBot.Db;
using NadekoBot.Db.Models;

namespace NadekoBot.Db.Models;

/// <summary>
/// Configuration for a live channel.
/// </summary>
public class LiveChannelConfig
{
    [Key]
    public int Id { get; set; }
    
    /// <summary>
    /// ID of the server this live channel belongs to.
    /// </summary>
    public ulong GuildId { get; set; }
    
    /// <summary>
    /// ID of the channel that is configured as a live channel.
    /// </summary>
    public ulong ChannelId { get; set; }
    
    /// <summary>
    /// Text template to be used for the live channel.
    /// </summary>
    public string Template { get; set; } = "";
}

public class LiveChannelConfigDbEntityTypeConfiguration : IEntityTypeConfiguration<LiveChannelConfig>
{
    public void Configure(EntityTypeBuilder<LiveChannelConfig> builder)
    {
        builder.HasIndex(x => x.GuildId);
        builder.HasIndex(x => new { x.GuildId, x.ChannelId }).IsUnique();
    }
}
