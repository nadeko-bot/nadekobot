#nullable disable
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;

namespace NadekoBot.Db.Models;

[DebuggerDisplay("{PrimaryTarget}{SecondaryTarget} {SecondaryTargetName} {State} {PrimaryTargetId}")]
public class Permissionv2 : DbEntity, IIndexed
{
    public ulong GuildId { get; set; }
    
    public int Index { get; set; }

    public PrimaryPermissionType PrimaryTarget { get; set; }
    public ulong PrimaryTargetId { get; set; }

    public SecondaryPermissionType SecondaryTarget { get; set; }
    public string SecondaryTargetName { get; set; }

    public bool IsCustomCommand { get; set; }

    public bool State { get; set; }

    [NotMapped]
    public static Permissionv2 AllowAllPerm
        => new()
        {
            PrimaryTarget = PrimaryPermissionType.Server,
            PrimaryTargetId = 0,
            SecondaryTarget = SecondaryPermissionType.AllModules,
            SecondaryTargetName = "*",
            State = true,
            Index = 0
        };

    public static List<Permissionv2> GetDefaultPermlist
        => [AllowAllPerm];
}

public enum PrimaryPermissionType
{
    User,
    Channel,
    Role,
    Server
}

public enum SecondaryPermissionType
{
    Module,
    Command,
    AllModules
}

public sealed class Permissionv2EntityConfiguration : IEntityTypeConfiguration<Permissionv2>
{
    public void Configure(EntityTypeBuilder<Permissionv2> builder)
    {
        builder.HasIndex(x => x.GuildId);
    }
}
