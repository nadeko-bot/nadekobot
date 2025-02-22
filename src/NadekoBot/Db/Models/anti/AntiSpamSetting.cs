using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NadekoBot.Db.Models;

#nullable disable
public class AntiSpamSetting : DbEntity
{
    public int GuildConfigId { get; set; }

    public ulong GuildId { get; set; }

    public PunishmentAction Action { get; set; }
    public int MessageThreshold { get; set; } = 3;
    public int MuteTime { get; set; }
    public ulong? RoleId { get; set; }
    public List<AntiSpamIgnore> IgnoredChannels { get; set; } = new();
}

// setup model 
public class AntiSpamEntityConfiguration : IEntityTypeConfiguration<AntiSpamSetting>
{
    public void Configure(EntityTypeBuilder<AntiSpamSetting> builder)
    {
        builder.HasIndex(x => x.GuildId)
               .IsUnique();

        builder.HasMany(x => x.IgnoredChannels)
               .WithOne()
               .OnDelete(DeleteBehavior.Cascade);
    }
}