using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NadekoBot.Modules.Utility.Scheduled;

public sealed class ScheduledCommand
{
    [Key]
    public int Id { get; set; }

    public ulong UserId { get; set; }
    public ulong ChannelId { get; set; }
    public ulong GuildId { get; set; }
    public ulong MessageId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime When { get; set; }
}

public sealed class ScheduledCommandEntityConfiguration : IEntityTypeConfiguration<ScheduledCommand>
{
    public void Configure(EntityTypeBuilder<ScheduledCommand> builder)
    {
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.GuildId);
        builder.HasIndex(x => x.When);
    }
}