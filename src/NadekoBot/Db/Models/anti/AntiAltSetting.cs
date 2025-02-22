using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NadekoBot.Db.Models;

public class AntiAltSetting
{
    [Key]
    public int Id { get; set; }

    public int GuildConfigId { get; set; }

    public ulong GuildId { get; set; }

    public TimeSpan MinAge { get; set; }
    public PunishmentAction Action { get; set; }
    public int ActionDurationMinutes { get; set; }
    public ulong? RoleId { get; set; }
}

public class AntiAltSettingEntityConfiguration : IEntityTypeConfiguration<AntiAltSetting>
{
    public void Configure(EntityTypeBuilder<AntiAltSetting> builder)
    {
        builder.HasIndex(x => x.GuildId)
               .IsUnique();
    }
}