using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NadekoBot.Modules.Xp;

public class ChannelXpConfig
{
    [Key]
    public int Id { get; set; }

    public XpRateType RateType { get; set; }
    public ulong GuildId { get; set; }
    public ulong ChannelId { get; set; }
    public long XpAmount { get; set; }
    public float Cooldown { get; set; }
}

public sealed class ChannelXpConfigEntity : IEntityTypeConfiguration<ChannelXpConfig>
{
    public void Configure(EntityTypeBuilder<ChannelXpConfig> builder)
    {
        builder.HasAlternateKey(x => new
        {
            x.GuildId,
            x.ChannelId,
            x.RateType,
        });
    }
}
