#nullable disable
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NadekoBot.Modules.Administration.Services;

namespace NadekoBot.Db.Models;

[ShardFiltered]
public class UnmuteTimer : DbEntity
{
    public ulong GuildId { get; set; }
    public ulong UserId { get; set; }
    public DateTime UnmuteAt { get; set; }
    public MuteType Type { get; set; } = MuteType.All;
}

public class UnmuteTimerEntityConfiguration : IEntityTypeConfiguration<UnmuteTimer>
{
    public void Configure(EntityTypeBuilder<UnmuteTimer> builder)
    {
        builder.HasIndex(x => new
        {
            x.GuildId,
            x.UserId,
            x.Type
        }).IsUnique();

        builder.Property(x => x.Type).HasDefaultValue(MuteType.All);
    }
}