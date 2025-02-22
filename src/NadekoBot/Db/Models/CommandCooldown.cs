#nullable disable
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NadekoBot.Db.Models;

public class CommandCooldown : DbEntity
{ 
    public ulong GuildId { get; set; }
    public int Seconds { get; set; }
    public string CommandName { get; set; }
}

public class CommandCooldownEntityConfiguration : IEntityTypeConfiguration<CommandCooldown>
{
    public void Configure(EntityTypeBuilder<CommandCooldown> builder)
    {
        builder.HasIndex(x => new
               {
                   x.GuildId,
                   x.CommandName
               })
               .IsUnique();
    }
}