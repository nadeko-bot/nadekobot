#nullable disable
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NadekoBot.Db.Models;

public class StreamRoleSettings : DbEntity
{
    public int GuildConfigId { get; set; }

    public ulong GuildId { get; set; }

    /// <summary>
    ///     Whether the feature is enabled in the guild.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    ///     Id of the role to give to the users in the role 'FromRole' when they start streaming
    /// </summary>
    public ulong AddRoleId { get; set; }

    /// <summary>
    ///     Id of the role whose users are eligible to get the 'AddRole'
    /// </summary>
    public ulong FromRoleId { get; set; }

    /// <summary>
    ///     If set, feature will only apply to users who have this keyword in their streaming status.
    /// </summary>
    public string Keyword { get; set; }

    /// <summary>
    ///     A collection of whitelisted users' IDs. Whitelisted users don't require 'keyword' in
    ///     order to get the stream role.
    /// </summary>
    public HashSet<StreamRoleWhitelistedUser> Whitelist { get; set; } = new();

    /// <summary>
    ///     A collection of blacklisted users' IDs. Blacklisted useres will never get the stream role.
    /// </summary>
    public HashSet<StreamRoleBlacklistedUser> Blacklist { get; set; } = new();
}

public class StreamRoleBlacklistedUser : DbEntity
{
    public int StreamRoleSettingsId { get; set; }
    public StreamRoleSettings StreamRoleSettings { get; set; }

    public ulong UserId { get; set; }
    public string Username { get; set; }

    public override bool Equals(object obj)
    {
        if (obj is not StreamRoleBlacklistedUser x)
            return false;

        return x.UserId == UserId;
    }

    public override int GetHashCode()
        => UserId.GetHashCode();
}

public class StreamRoleWhitelistedUser : DbEntity
{
    public int StreamRoleSettingsId { get; set; }
    public StreamRoleSettings StreamRoleSettings { get; set; }

    public ulong UserId { get; set; }
    public string Username { get; set; }

    public override bool Equals(object obj)
        => obj is StreamRoleWhitelistedUser x ? x.UserId == UserId : false;

    public override int GetHashCode()
        => UserId.GetHashCode();
}

public class StreamRoleSettingsEntityConfiguration : IEntityTypeConfiguration<StreamRoleSettings>
{
    public void Configure(EntityTypeBuilder<StreamRoleSettings> builder)
    {
        builder.HasIndex(x => x.GuildId)
               .IsUnique();

        builder
            .HasMany(x => x.Whitelist)
            .WithOne(x => x.StreamRoleSettings)
            .HasForeignKey(x => x.StreamRoleSettingsId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasMany(x => x.Blacklist)
            .WithOne(x => x.StreamRoleSettings)
            .HasForeignKey(x => x.StreamRoleSettingsId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}