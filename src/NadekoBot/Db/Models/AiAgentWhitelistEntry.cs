using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NadekoBot.Db.Models;

/// <summary>
/// Kind of discord entity a whitelist entry points at. Adding a new kind requires no
/// schema change - only a new member here and a matching lookup in the whitelist service.
/// </summary>
public enum AiAgentWhitelistType
{
    User = 0,
    Server = 1,
    Role = 2,
    Channel = 3
}

/// <summary>
/// Grants AI agent access to a discord entity. Bot-wide, managed by the bot owner.
/// </summary>
public class AiAgentWhitelistEntry
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Id of the whitelisted entity. Meaning depends on <see cref="Type"/>.
    /// </summary>
    public ulong ItemId { get; set; }

    public AiAgentWhitelistType Type { get; set; }
}

public sealed class AiAgentWhitelistEntryEntityConfiguration : IEntityTypeConfiguration<AiAgentWhitelistEntry>
{
    public void Configure(EntityTypeBuilder<AiAgentWhitelistEntry> builder)
    {
        builder.HasIndex(x => new { x.Type, x.ItemId }).IsUnique();
    }
}
