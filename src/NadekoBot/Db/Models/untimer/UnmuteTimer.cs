#nullable disable
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.ComponentModel.DataAnnotations;

namespace NadekoBot.Db.Models;

public class UnmuteTimer : DbEntity
{
    public ulong GuildId { get; set; }
    public ulong UserId { get; set; }
    public DateTime UnmuteAt { get; set; }
}

public class UnmuteTimerEntityConfiguration : IEntityTypeConfiguration<UnmuteTimer>
{
    public void Configure(EntityTypeBuilder<UnmuteTimer> builder)
    {
        builder.HasIndex(x => new
        {
            x.GuildId,
            x.UserId
        }).IsUnique();
    }
}