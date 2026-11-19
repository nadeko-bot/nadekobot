using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NadekoBot.Modules.Gambling.Common.AnimalRacing;

/// <summary>
/// A member in an animal race which did not end yet, with the bet which the bot took.
/// The bot deletes the row when it pays the race out or refunds it.
/// </summary>
[ShardFiltered]
public sealed class AnimalRaceEntry
{
    /// <summary>
    /// Primary key. The order of the ids is the join order.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Id of the server of the race.
    /// </summary>
    public ulong GuildId { get; set; }

    /// <summary>
    /// Id of the channel of the race.
    /// </summary>
    public ulong ChannelId { get; set; }

    /// <summary>
    /// Id of the member who joined.
    /// </summary>
    public ulong UserId { get; set; }

    /// <summary>
    /// Amount of currency which the bot took. 0 for a join without a bet.
    /// </summary>
    public long Amount { get; set; }

    /// <summary>
    /// When the member joined.
    /// </summary>
    public DateTime DateAdded { get; set; }
}

public sealed class AnimalRaceEntryConfiguration : IEntityTypeConfiguration<AnimalRaceEntry>
{
    public void Configure(EntityTypeBuilder<AnimalRaceEntry> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.GuildId, x.UserId }).IsUnique();
    }
}
