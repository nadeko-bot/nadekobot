#nullable disable
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NadekoBot.Db.Models;

[ShardFiltered]
public class FeedSub : DbEntity
{
    public ulong GuildId { get; set; }

    public ulong ChannelId { get; set; }
    public string Url { get; set; }

    public string Message { get; set; }

    /// <summary>
    /// UTC time of the first failed check in the current failure streak. Null while the feed works.
    /// </summary>
    public DateTime? FailingSince { get; set; }
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