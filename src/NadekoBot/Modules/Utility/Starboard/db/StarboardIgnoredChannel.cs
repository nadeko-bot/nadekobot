using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NadekoBot.Db;

namespace NadekoBot.Modules.Utility.Starboard.Db;

[ShardFiltered]
public class StarboardIgnoredChannel
{
    [Key]
    public int Id { get; set; }

    public ulong GuildId { get; set; }

    public ulong ChannelId { get; set; }
}

public sealed class StarboardIgnoredChannelEntityConfiguration : IEntityTypeConfiguration<StarboardIgnoredChannel>
{
    public void Configure(EntityTypeBuilder<StarboardIgnoredChannel> builder)
    {
        builder.HasIndex(x => new { x.GuildId, x.ChannelId })
               .IsUnique();
    }
}
