using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.ComponentModel.DataAnnotations;

namespace NadekoBot.Db.Models;

public class SlowmodeIgnoredRole : DbEntity
{
    public ulong GuildId { get; set; }
    public ulong RoleId { get; set; }
}

public class SlowmodeIgnoredRoleEntityConfiguration : IEntityTypeConfiguration<SlowmodeIgnoredRole>
{
    public void Configure(EntityTypeBuilder<SlowmodeIgnoredRole> builder)
    {
        builder.HasIndex(x => new
               {
                   x.GuildId,
                   x.RoleId
               })
               .IsUnique();
    }
}