using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NadekoBot.Db;

namespace NadekoBot.Modules.Utility.Starboard.Db;

[ShardFiltered]
public class StarboardConfig
{
    [Key]
    public int Id { get; set; }

    public ulong GuildId { get; set; }

    public ulong ChannelId { get; set; }

    public string Emote { get; set; } = null!;

    public int Threshold { get; set; }

    public bool AllowSelfStar { get; set; }

    public bool AllowBots { get; set; }

    public bool IsEnabled { get; set; }

    /// <summary>
    /// How many starred messages the starboard shows. The lowest ranked entries above
    /// this count are dropped from the board.
    /// </summary>
    public int Limit { get; set; }
}

public sealed class StarboardConfigEntityConfiguration : IEntityTypeConfiguration<StarboardConfig>
{
    public void Configure(EntityTypeBuilder<StarboardConfig> builder)
    {
        builder.HasIndex(x => x.GuildId)
               .IsUnique();

        builder.Property(x => x.Emote)
               .HasMaxLength(StarboardConsts.MAX_EMOTE_LENGTH)
               .HasDefaultValue(StarboardConsts.DEFAULT_EMOTE);

        builder.Property(x => x.Threshold)
               .HasDefaultValue(StarboardConsts.DEFAULT_THRESHOLD);

        builder.Property(x => x.Limit)
               .HasDefaultValue(StarboardConsts.DEFAULT_LIMIT);

        builder.Property(x => x.IsEnabled)
               .HasDefaultValue(true);

        builder.Property(x => x.AllowSelfStar)
               .HasDefaultValue(false);

        builder.Property(x => x.AllowBots)
               .HasDefaultValue(false);
    }
}

public static class StarboardConsts
{
    public const string DEFAULT_EMOTE = "⭐";
    public const int DEFAULT_THRESHOLD = 3;
    public const int MIN_THRESHOLD = 3;
    public const int MAX_THRESHOLD = 100;
    public const int MAX_EMOTE_LENGTH = 100;
    public const int MAX_IGNORED_CHANNELS = 25;
    public const int MAX_EMBEDS_PER_MESSAGE = 10;

    public const int DEFAULT_LIMIT = 100;
    public const int MIN_LIMIT = 10;
    public const int MAX_LIMIT = 100;

    // Discord counts all embeds of a message against a single 6000 character budget.
    // Ten embeds share it, and the reserve covers the star counts, which grow while
    // the message stays up.
    public const int EMBED_CHAR_BUDGET = 550;
}
