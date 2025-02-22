#nullable disable
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NadekoBot.Db.Models;

public class FeedSub : DbEntity
{
    public ulong GuildId { get; set; }

    public ulong ChannelId { get; set; }
    public string Url { get; set; }

    public string Message { get; set; }
}

public sealed class FeedSubEntityConfiguration : IEntityTypeConfiguration<FeedSub>
{
    public void Configure(EntityTypeBuilder<FeedSub> builder)
    {
        builder
            .HasIndex(x => new
            {
                x.GuildId,
                x.Url
            })
            .IsUnique();
    }
}