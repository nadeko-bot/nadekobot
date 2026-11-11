using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NadekoBot.Db.Models;

public sealed class UserReputation
{
    /// <summary>
    /// Primary key.
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Server in which the user has this reputation.
    /// </summary>
    public ulong GuildId { get; set; }

    /// <summary>
    /// User who owns the reputation and the give cooldown.
    /// </summary>
    public ulong UserId { get; set; }

    /// <summary>
    /// Number of reputation points other members gave this user.
    /// </summary>
    public int Reputation { get; set; }

    /// <summary>
    /// When this user last gave a reputation point in this server, in UTC.
    /// </summary>
    public DateTime? LastGivenAt { get; set; }
}

public sealed class UserReputationEntityConfiguration : IEntityTypeConfiguration<UserReputation>
{
    public void Configure(EntityTypeBuilder<UserReputation> builder)
    {
        builder.HasIndex(x => new { x.GuildId, x.UserId })
               .IsUnique();

        builder.HasIndex(x => new { x.GuildId, x.Reputation });
    }
}
