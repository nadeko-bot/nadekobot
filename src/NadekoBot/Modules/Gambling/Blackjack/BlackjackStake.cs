using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NadekoBot.Modules.Gambling.Blackjack;

public enum BjStakeKind : byte
{
    Bet,
    Split,
    Double
}

/// <summary>
/// Currency which the bot took from a member for a blackjack round which is not settled yet.
/// The bot deletes the row when it pays the round out, and refunds it when the round never finishes.
/// </summary>
[ShardFiltered]
public sealed class BlackjackStake
{
    /// <summary>
    /// Primary key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Id of the server of the table.
    /// </summary>
    public ulong GuildId { get; set; }

    /// <summary>
    /// Id of the channel of the table.
    /// </summary>
    public ulong ChannelId { get; set; }

    /// <summary>
    /// Id of the member who placed the stake.
    /// </summary>
    public ulong UserId { get; set; }

    /// <summary>
    /// What the stake paid for: the bet, a split or a double.
    /// </summary>
    public BjStakeKind Kind { get; set; }

    /// <summary>
    /// Amount of currency which the bot took.
    /// </summary>
    public long Amount { get; set; }

    /// <summary>
    /// When the bot took the currency.
    /// </summary>
    public DateTime DateAdded { get; set; }
}

public sealed class BlackjackStakeConfiguration : IEntityTypeConfiguration<BlackjackStake>
{
    public void Configure(EntityTypeBuilder<BlackjackStake> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => new { x.ChannelId, x.UserId });
    }
}

/// <summary>
/// The cards of a blackjack round which is not settled yet, so a restart can finish the round
/// instead of refunding it. The bot deletes the row when it settles the round.
/// </summary>
[ShardFiltered]
public sealed class BlackjackRoundState
{
    /// <summary>
    /// Primary key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Id of the server of the table.
    /// </summary>
    public ulong GuildId { get; set; }

    /// <summary>
    /// Id of the channel of the table. One table at a time plays in a channel.
    /// </summary>
    public ulong ChannelId { get; set; }

    /// <summary>
    /// The round, in the format of <see cref="BlackjackRoundStateCodec" />.
    /// </summary>
    public byte[] Cards { get; set; } = null!;

    /// <summary>
    /// When the bot last wrote the round.
    /// </summary>
    public DateTime DateAdded { get; set; }
}

public sealed class BlackjackRoundStateConfiguration : IEntityTypeConfiguration<BlackjackRoundState>
{
    public void Configure(EntityTypeBuilder<BlackjackRoundState> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.ChannelId).IsUnique();
    }
}
