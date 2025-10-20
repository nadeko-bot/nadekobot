using System.ComponentModel.DataAnnotations;

namespace NadekoBot.Db.Models;

public class StarboardSetting : DbEntity
{
    public ulong GuildId { get; set; }

    // Target channel where starboard posts are sent
    public ulong? StarboardChannelId { get; set; }

    // Primary emoji used for counting (may be unicode or custom emote string)
    [MaxLength(100)]
    public string Emoji { get; set; } = "⭐";

    // Minimum reactions required
    public int Threshold { get; set; } = 5;

    // Whether users can star their own messages
    public bool AllowSelfStar { get; set; } = false;

    // Whether bot messages can be starred
    public bool AllowBotMessages { get; set; } = false;

    // When true, only the configured Emoji counts; otherwise any emoji counts
    public bool StrictEmoji { get; set; } = true;

    // Whether the feature is enabled for the guild
    public bool IsEnabled { get; set; } = false;
}

public class StarboardIgnoredChannel : DbEntity
{
    public ulong GuildId { get; set; }
    public ulong ChannelId { get; set; }
}

public class StarboardChannelOverride : DbEntity
{
    public ulong GuildId { get; set; }
    public ulong ChannelId { get; set; }

    public int? Threshold { get; set; }
}

public class StarboardMessage : DbEntity
{
    public ulong GuildId { get; set; }
    public ulong ChannelId { get; set; }
    public ulong SourceMessageId { get; set; }

    // The message posted in the starboard channel
    public ulong? StarboardMessageId { get; set; }

    public int StarCount { get; set; }

    // Cached snapshot to avoid fetching too often
    [MaxLength(2000)]
    public string? SnapshotContent { get; set; }
    public ulong AuthorId { get; set; }
}
