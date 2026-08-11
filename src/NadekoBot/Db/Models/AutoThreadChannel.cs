using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NadekoBot.Db.Models;

public enum AutoThreadMode
{
    All = 0,
    Media = 1,
}

[ShardFiltered]
public class AutoThreadChannel
{
    [Key]
    public int Id { get; set; }

    public ulong GuildId { get; set; }

    public ulong ChannelId { get; set; }

    public AutoThreadMode Mode { get; set; }

    public int ArchiveDurationMinutes { get; set; }
}

public sealed class AutoThreadChannelEntityConfiguration : IEntityTypeConfiguration<AutoThreadChannel>
{
    public void Configure(EntityTypeBuilder<AutoThreadChannel> builder)
    {
        builder.HasIndex(x => x.ChannelId).IsUnique();
        builder.HasIndex(x => x.GuildId);
    }
}
