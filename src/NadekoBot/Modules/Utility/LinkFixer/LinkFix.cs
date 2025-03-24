using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NadekoBot.Db.Models;

/// <summary>
/// Represents a link fix configuration for a guild
/// </summary>
public class LinkFix
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// ID of the guild this link fix belongs to
    /// </summary>
    public ulong GuildId { get; set; }
    
    /// <summary>
    /// The domain to be replaced
    /// </summary>
    public string OldDomain { get; set; } = null!;
    
    /// <summary>
    /// The domain to replace with
    /// </summary>
    public string NewDomain { get; set; } = null!;
}

/// <summary>
/// Entity configuration for <see cref="LinkFix"/>
/// </summary>
public class LinkFixConfiguration : IEntityTypeConfiguration<LinkFix>
{
    public void Configure(EntityTypeBuilder<LinkFix> builder)
    {
        builder.HasIndex(x => new { x.GuildId, x.OldDomain }).IsUnique();
    }
}