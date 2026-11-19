using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NadekoBot.Modules.Games.SavedGames;

public enum SavedGameKind : byte
{
    Hangman = 1,
    Trivia = 2,
    AnimalRace = 3
}

/// <summary>
/// A game which runs in a channel, so the bot can continue it after a restart.
/// The bot deletes the row when the game ends.
/// </summary>
[ShardFiltered]
public sealed class SavedGame
{
    /// <summary>
    /// Primary key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Id of the server of the game.
    /// </summary>
    public ulong GuildId { get; set; }

    /// <summary>
    /// Id of the channel of the game.
    /// </summary>
    public ulong ChannelId { get; set; }

    /// <summary>
    /// The game which the row holds.
    /// </summary>
    public SavedGameKind Kind { get; set; }

    /// <summary>
    /// The state of the game as UTF-8 JSON, in the format of <see cref="SavedGameJsonContext" />.
    /// </summary>
    public byte[] State { get; set; } = null!;

    /// <summary>
    /// When the bot last wrote the state.
    /// </summary>
    public DateTime DateAdded { get; set; }
}

public sealed class SavedGameConfiguration : IEntityTypeConfiguration<SavedGame>
{
    public void Configure(EntityTypeBuilder<SavedGame> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.ChannelId, x.Kind }).IsUnique();
        builder.HasIndex(x => new { x.GuildId, x.Kind });
    }
}
