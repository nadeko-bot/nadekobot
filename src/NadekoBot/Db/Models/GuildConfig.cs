using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.ComponentModel.DataAnnotations;

namespace NadekoBot.Db.Models;
public class GuildFilterConfig
{
    [Key]
    public int Id { get; set; }
    
    public ulong GuildId { get; set; }
    public bool FilterInvites { get; set; }
    public bool FilterLinks { get; set; }
    public bool FilterWords { get; set; }
    public HashSet<FilterChannelId> FilterInvitesChannelIds { get; set; } = new();
    public HashSet<FilterLinksChannelId> FilterLinksChannelIds { get; set; } = new();
    public HashSet<FilteredWord> FilteredWords { get; set; } = new();
    public HashSet<FilterWordsChannelId> FilterWordsChannelIds { get; set; } = new();
}

public sealed class GuildFilterConfigEntityConfiguration : IEntityTypeConfiguration<GuildFilterConfig>
{
    public void Configure(EntityTypeBuilder<GuildFilterConfig> builder)
    {
        builder.HasIndex(x => x.GuildId);
    }
}

public class GuildConfig : DbEntity
{
    public ulong GuildId { get; set; }
    public string? Prefix { get; set; } = null;

    public bool DeleteMessageOnCommand { get; set; } = false;

    public string? AutoAssignRoleIds { get; set; } = null;
    public bool VerbosePermissions { get; set; } = true;
    public string? PermissionRole { get; set; } = null;

    //filtering
    public string? MuteRoleName { get; set; } = null;

    // chatterbot
    public bool CleverbotEnabled { get; set; } = false;

    // aliases
    public bool WarningsInitialized { get; set; } = false;

    public ulong? GameVoiceChannel { get; set; } = null;
    public bool VerboseErrors { get; set; } = true;


    public bool NotifyStreamOffline { get; set; } = true;
    public bool DeleteStreamOnlineMessage { get; set; } = false;
    public int WarnExpireHours { get; set; } = 0;
    public WarnExpireAction WarnExpireAction { get; set; } = WarnExpireAction.Clear;

    public bool DisableGlobalExpressions { get; set; } = false;

    public bool StickyRoles { get; set; } = false;

    public string? TimeZoneId { get; set; } = null;
    public string? Locale { get; set; } = null;

    public List<Permissionv2> Permissions { get; set; } = [];
}