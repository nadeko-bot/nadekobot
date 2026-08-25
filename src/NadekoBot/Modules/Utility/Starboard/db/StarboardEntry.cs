using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NadekoBot.Modules.Utility.Starboard.Db;

public class StarboardEntry
{
    [Key]
    public int Id { get; set; }

    public ulong GuildId { get; set; }

    public ulong ChannelId { get; set; }

    public ulong MessageId { get; set; }

    public int StarCount { get; set; }

    /// <summary>
    /// Rank of the entry on the board, starting at 0 for the most starred message.
    /// The position decides which starboard message holds the entry and in which slot,
    /// so the board stays sorted by star count.
    /// </summary>
    public int Position { get; set; }
}

public sealed class StarboardEntryEntityConfiguration : IEntityTypeConfiguration<StarboardEntry>
{
    public void Configure(EntityTypeBuilder<StarboardEntry> builder)
    {
        builder.HasIndex(x => new { x.GuildId, x.MessageId })
               .IsUnique();

        // Not unique on purpose: shifting a range of positions updates row by row, so a unique
        // index would trip on a value the statement is about to move out of the way. The write
        // path of a guild is serialized, which is what actually keeps positions distinct.
        builder.HasIndex(x => new { x.GuildId, x.Position });
    }
}
