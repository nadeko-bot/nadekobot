using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NadekoBot.Modules.Utility.UserRole;

/// <summary>
/// Represents a user's assigned role in a guild
/// </summary>
public class UserRole
{
    /// <summary>
    /// ID of the guild
    /// </summary>
    public ulong GuildId { get; set; }

    /// <summary>
    /// ID of the user
    /// </summary>
    public ulong UserId { get; set; }

    /// <summary>
    /// ID of the Discord role
    /// </summary>
    public ulong RoleId { get; set; }
}

public class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        // Set composite primary key
        builder.HasKey(x => new { x.GuildId, x.UserId, x.RoleId });
        
        // Create indexes for frequently queried columns
        builder.HasIndex(x => x.GuildId);
        builder.HasIndex(x => new { x.GuildId, x.UserId });
    }
}
