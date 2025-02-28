using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NadekoBot.Modules.Xp;

public class GuildXpConfig
{
    [Key]
    public int Id { get; set; }
    
    public ulong GuildId { get; set; }
    public XpRateType RateType { get; set; }
    
    public long XpAmount { get; set; }
    public float Cooldown { get; set; }
    public string? XpTemplateUrl { get; set; }
}

public sealed class GuildXpConfigEntity : IEntityTypeConfiguration<GuildXpConfig>
{
    public void Configure(EntityTypeBuilder<GuildXpConfig> builder)
    {
        builder.HasAlternateKey(x => new
        {
            x.GuildId,
            x.RateType,
        });
    }
}
