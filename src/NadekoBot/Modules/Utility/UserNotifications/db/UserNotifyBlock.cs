using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NadekoBot.Modules.Utility.UserNotifications.Db;

public class UserNotifyBlock
{
    public int Id { get; set; }

    public ulong UserId { get; set; }

    public string Type { get; set; } = null!;
}

public class UserNotifyBlockEntityConfiguration : IEntityTypeConfiguration<UserNotifyBlock>
{
    public void Configure(EntityTypeBuilder<UserNotifyBlock> builder)
    {
        builder.HasIndex(x => new { x.UserId, x.Type }).IsUnique();
        builder.Property(x => x.Type).HasMaxLength(128);
    }
}
