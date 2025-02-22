#nullable disable
namespace NadekoBot.Db.Models;

public class MusicPlayerSettings
{
    /// <summary>
    ///     Auto generated Id
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    ///     Id of the guild
    /// </summary>
    public ulong GuildId { get; set; }

    /// <summary>
    ///     Queue repeat type
    /// </summary>
    public PlayerRepeatType PlayerRepeat { get; set; } = PlayerRepeatType.Queue;

    /// <summary>
    ///     Channel id the bot will always try to send track related messages to
    /// </summary>
    public ulong? MusicChannelId { get; set; }

    /// <summary>
    ///     Default volume player will be created with
    /// </summary>
    public int Volume { get; set; } = 100;

    /// <summary>
    ///     Whether the bot should auto disconnect from the voice channel once the queue is done
    ///     This only has effect if
    /// </summary>
    public bool AutoDisconnect { get; set; }

    /// <summary>
    ///     Selected quality preset for the music player
    /// </summary>
    public QualityPreset QualityPreset { get; set; }

    /// <summary>
    ///     Whether the bot will automatically queue related songs
    /// </summary>
    public bool AutoPlay { get; set; }
}

public enum QualityPreset
{
    Highest,
    High,
    Medium,
    Low
}

public enum PlayerRepeatType
{
    None,
    Track,
    Queue
}