using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NadekoBot.Db.Models;

public class LogIgnore
{
    [Key]
    public int Id { get; set; }

    public ulong GuildId { get; set; }
    public ulong LogItemId { get; set; }
    public IgnoredItemType ItemType { get; set; }
}

public enum IgnoredItemType
{
    Channel,
    User,
    Category
}

public sealed class LogIgnoreEntityConfiguration : IEntityTypeConfiguration<LogIgnore>
{
    public void Configure(EntityTypeBuilder<LogIgnore> builder)
    {
        builder.HasIndex(x => new { x.GuildId, x.LogItemId, x.ItemType }).IsUnique();
    }
}
