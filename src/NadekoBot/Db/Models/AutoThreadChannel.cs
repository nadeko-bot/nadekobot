using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NadekoBot.Db.Models;

public enum AutoThreadMode
{
    All = 0,
    Media = 1,
}

[ShardFiltered]
public class AutoThreadChannel
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// The server of the channel.
    /// </summary>
    public ulong GuildId { get; set; }

    /// <summary>
    /// The text channel in which the bot creates a thread for each new message.
    /// </summary>
    public ulong ChannelId { get; set; }

    /// <summary>
    /// Which messages start a thread.
    /// </summary>
    public AutoThreadMode Mode { get; set; }

    /// <summary>
    /// Inactive minutes after which Discord archives a created thread.
    /// </summary>
    public int ArchiveDurationMinutes { get; set; }

    /// <summary>
    /// Whether messages of bots, webhooks and the bot itself also start a thread.
    /// </summary>
    public bool IncludeBots { get; set; }
}

public sealed class AutoThreadChannelEntityConfiguration : IEntityTypeConfiguration<AutoThreadChannel>
{
    public void Configure(EntityTypeBuilder<AutoThreadChannel> builder)
    {
        builder.HasIndex(x => x.ChannelId).IsUnique();
        builder.HasIndex(x => x.GuildId);
    }
}
