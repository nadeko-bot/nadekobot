using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NadekoBot.Db.Models;

/// <summary>
/// A domain whose links the link filter lets through on one server.
/// </summary>
[ShardFiltered]
public sealed class AllowedLinkDomain
{
    public const int MAX_DOMAIN_LENGTH = 253;

    /// <summary>
    /// Primary key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// The server which allows the domain.
    /// </summary>
    public ulong GuildId { get; set; }

    /// <summary>
    /// Lowercase ASCII domain, for example youtube.com. It also allows its subdomains.
    /// </summary>
    public string Domain { get; set; } = null!;
}

public sealed class AllowedLinkDomainEntityConfiguration : IEntityTypeConfiguration<AllowedLinkDomain>
{
    public void Configure(EntityTypeBuilder<AllowedLinkDomain> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Domain)
               .HasMaxLength(AllowedLinkDomain.MAX_DOMAIN_LENGTH);

        builder.HasIndex(x => new
               {
                   x.GuildId,
                   x.Domain
               })
               .IsUnique();
    }
}
