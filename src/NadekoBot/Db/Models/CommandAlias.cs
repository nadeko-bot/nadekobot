#nullable disable
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.ComponentModel.DataAnnotations;

namespace NadekoBot.Db.Models;

public class CommandAlias : DbEntity
{
    public ulong GuildId { get; set; }
    public string Trigger { get; set; }
    public string Mapping { get; set; }
}

public class CommandAliasEntityConfiguration : IEntityTypeConfiguration<CommandAlias>
{
    public void Configure(EntityTypeBuilder<CommandAlias> builder)
    {
        builder.HasIndex(x => x.GuildId);
    }
}