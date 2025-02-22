using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NadekoBot.Db.Models;

public class XpSettingsEntityConfiguration : IEntityTypeConfiguration<XpSettings>
{
    public void Configure(EntityTypeBuilder<XpSettings> builder)
    {
        builder.HasIndex(x => x.GuildId)
               .IsUnique();

        builder.HasMany(x => x.CurrencyRewards)
               .WithOne();
        
        builder.HasMany(x => x.RoleRewards)
               .WithOne();

        builder.HasMany(x => x.ExclusionList)
               .WithOne();
    }
}