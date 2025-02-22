using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.ComponentModel.DataAnnotations;

namespace NadekoBot.Db.Models;

public class VcRoleInfo : DbEntity
{
    public ulong GuildId { get; set; }
    public ulong VoiceChannelId { get; set; }
    
    public ulong RoleId { get; set; }
}

public class VcRoleInfoEntityConfiguration : IEntityTypeConfiguration<VcRoleInfo>
{
    public void Configure(EntityTypeBuilder<VcRoleInfo> builder)
    {
        builder.HasIndex(x => new
               {
                   x.GuildId,
                   x.VoiceChannelId
               })
               .IsUnique();
    }
}