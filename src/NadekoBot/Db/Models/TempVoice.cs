using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NadekoBot.Db.Models;

/// <summary>
/// A voice channel which creates a temporary voice channel for each member who joins it.
/// </summary>
[ShardFiltered]
public class TempVoiceHub
{
    /// <summary>
    /// Primary key.
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Id of the server which has the hub.
    /// </summary>
    public ulong GuildId { get; set; }

    /// <summary>
    /// Id of the hub voice channel.
    /// </summary>
    public ulong ChannelId { get; set; }
}

/// <summary>
/// A temporary voice channel which the bot created from a hub.
/// </summary>
[ShardFiltered]
public class TempVoiceChannel
{
    /// <summary>
    /// Primary key.
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Id of the server which has the channel.
    /// </summary>
    public ulong GuildId { get; set; }

    /// <summary>
    /// Id of the temporary voice channel.
    /// </summary>
    public ulong ChannelId { get; set; }

    /// <summary>
    /// Id of the member for whom the bot created the channel.
    /// </summary>
    public ulong OwnerId { get; set; }
}

public sealed class TempVoiceHubEntityConfiguration : IEntityTypeConfiguration<TempVoiceHub>
{
    public void Configure(EntityTypeBuilder<TempVoiceHub> builder)
    {
        builder.HasIndex(x => x.ChannelId).IsUnique();
        builder.HasIndex(x => x.GuildId);
    }
}

public sealed class TempVoiceChannelEntityConfiguration : IEntityTypeConfiguration<TempVoiceChannel>
{
    public void Configure(EntityTypeBuilder<TempVoiceChannel> builder)
    {
        builder.HasIndex(x => x.ChannelId).IsUnique();
        builder.HasIndex(x => new { x.GuildId, x.OwnerId });
    }
}
