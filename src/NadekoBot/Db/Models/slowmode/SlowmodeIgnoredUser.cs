using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.ComponentModel.DataAnnotations;

namespace NadekoBot.Db.Models;

public class SlowmodeIgnoredUser : DbEntity
{
    public ulong GuildId { get; set; }
    public ulong UserId { get; set; }
}

public class SlowmodeIgnoredUserEntityConfiguration : IEntityTypeConfiguration<SlowmodeIgnoredUser>
{
    public void Configure(EntityTypeBuilder<SlowmodeIgnoredUser> builder)
    {
        builder.HasIndex(x => new
               {
                   x.GuildId,
                   x.UserId
               })
               .IsUnique();
    }
}