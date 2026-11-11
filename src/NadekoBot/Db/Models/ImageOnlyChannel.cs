#nullable disable
namespace NadekoBot.Db.Models;

public class ImageOnlyChannel : DbEntity
{
    public ulong GuildId { get; set; }
    public ulong ChannelId { get; set; }
    public OnlyChannelType Type { get; set; }

    /// <summary>
    /// What the bot does to a member who posts a message of the wrong kind.
    /// </summary>
    public OnlyChannelPunishment Punishment { get; set; }
}

public enum OnlyChannelType
{
    Image,
    Link
}

public enum OnlyChannelPunishment
{
    // deletes the message, and after 3 messages stops the member from typing in the channel
    Lock = 0,

    // only deletes the message
    Delete = 1,
}
