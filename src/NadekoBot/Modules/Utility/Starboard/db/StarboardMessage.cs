using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NadekoBot.Modules.Utility.Starboard.Db;

/// <summary>
/// One message of the starboard channel. Each holds up to
/// <see cref="StarboardConsts.MAX_EMBEDS_PER_MESSAGE"/> entries, so the entry at position P
/// lives in the message with index P / MAX_EMBEDS_PER_MESSAGE.
/// </summary>
public class StarboardMessage
{
    [Key]
    public int Id { get; set; }

    public ulong GuildId { get; set; }

    /// <summary>
    /// Position of the message on the board, starting at 0 for the message which holds
    /// the most starred entries.
    /// </summary>
    public int Index { get; set; }

    public ulong MessageId { get; set; }
}

public sealed class StarboardMessageEntityConfiguration : IEntityTypeConfiguration<StarboardMessage>
{
    public void Configure(EntityTypeBuilder<StarboardMessage> builder)
        => builder.HasIndex(x => new { x.GuildId, x.Index })
                  .IsUnique();
}
