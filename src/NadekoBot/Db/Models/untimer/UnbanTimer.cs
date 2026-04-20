#nullable disable
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NadekoBot.Db.Models;

[ShardFiltered]
public class UnbanTimer : DbEntity
{
    public ulong GuildId { get; set; }
    public ulong UserId { get; set; }
    public DateTime UnbanAt { get; set; }
}

public class UnbanTimerEntityConfiguration : IEntityTypeConfiguration<UnbanTimer>
{
    public void Configure(EntityTypeBuilder<UnbanTimer> builder)
    {
        builder.HasIndex(x => new
               {
                   x.GuildId,
                   x.UserId
               })
               .IsUnique();
    }
}