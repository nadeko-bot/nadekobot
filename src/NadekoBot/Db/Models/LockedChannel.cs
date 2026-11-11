using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NadekoBot.Db.Models;

/// <summary>
/// A channel which <c>.lock</c> locked, with the @everyone overwrite bits which <c>.unlock</c> restores.
/// </summary>
public sealed class LockedChannel
{
    /// <summary>
    /// Primary key.
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Server of the channel.
    /// </summary>
    public ulong GuildId { get; set; }

    /// <summary>
    /// The locked channel.
    /// </summary>
    public ulong ChannelId { get; set; }

    /// <summary>
    /// The locked permission bits which the @everyone overwrite allowed before the lock.
    /// </summary>
    public ulong AllowMask { get; set; }

    /// <summary>
    /// The locked permission bits which the @everyone overwrite denied before the lock.
    /// </summary>
    public ulong DenyMask { get; set; }
}

public sealed class LockedChannelEntityConfiguration : IEntityTypeConfiguration<LockedChannel>
{
    public void Configure(EntityTypeBuilder<LockedChannel> builder)
    {
        builder.HasIndex(x => x.ChannelId).IsUnique();
        builder.HasIndex(x => x.GuildId);
    }
}
