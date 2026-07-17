using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NadekoBot.Common;

namespace NadekoBot.Db.Models;

[ShardFiltered]
public class LogChannel
{
    [Key]
    public int Id { get; set; }

    public ulong GuildId { get; set; }
    public LogType LogType { get; set; }
    public ulong ChannelId { get; set; }
}

public sealed class LogChannelEntityConfiguration : IEntityTypeConfiguration<LogChannel>
{
    public void Configure(EntityTypeBuilder<LogChannel> builder)
    {
        builder.HasIndex(x => new { x.GuildId, x.LogType }).IsUnique();
    }
}
