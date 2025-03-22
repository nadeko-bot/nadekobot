using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NadekoBot.Db.Models;

public class XpExcludedItem
{
    [Key]
    public int Id { get; set; }

    public ulong GuildId { get; set; }

    public XpExcludedItemType ItemType { get; set; }
    public ulong ItemId { get; set; }
}

public sealed class XpExclusionEntityConfig : IEntityTypeConfiguration<XpExcludedItem>
{
    public void Configure(EntityTypeBuilder<XpExcludedItem> builder)
    {
        builder.HasIndex(x => x.GuildId);

        builder.HasAlternateKey(x => new
        {
            x.GuildId,
            x.ItemType,
            x.ItemId
        });
    }
}