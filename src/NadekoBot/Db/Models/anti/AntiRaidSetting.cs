#nullable disable
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.ComponentModel.DataAnnotations.Schema;

namespace NadekoBot.Db.Models;

public class AntiRaidSetting : DbEntity
{
    public ulong GuildId { get; set; }
    public int UserThreshold { get; set; }
    public int Seconds { get; set; }
    public PunishmentAction Action { get; set; }

    /// <summary>
    ///     Duration of the punishment, in minutes. This works only for supported Actions, like:
    ///     Mute, Chatmute, Voicemute, etc...
    /// </summary>
    public int PunishDuration { get; set; }
}

public class AntiRaidSettingEntityConfiguration : IEntityTypeConfiguration<AntiRaidSetting>
{
    public void Configure(EntityTypeBuilder<AntiRaidSetting> builder)
    {
        builder.HasIndex(x => x.GuildId)
               .IsUnique();
    }
}