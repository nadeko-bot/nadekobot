#nullable disable
namespace NadekoBot.Db.Models;

public class CurrencyEvent
{
    public enum Type
    {
        Reaction,

        GameStatus
        //NotRaid,
    }

    public ulong ServerId { get; set; }
    public ulong ChannelId { get; set; }
    public ulong MessageId { get; set; }
    public Type EventType { get; set; }

    /// <summary>
    ///     Amount of currency that the user will be rewarded.
    /// </summary>
    public long Amount { get; set; }

    /// <summary>
    ///     Maximum amount of currency that can be handed out.
    /// </summary>
    public long PotSize { get; set; }

    public List<AwardedUser> AwardedUsers { get; set; }

    /// <summary>
    ///     Used as extra data storage for events which need it.
    /// </summary>
    public ulong ExtraId { get; set; }

    /// <summary>
    ///     May be used for some future event.
    /// </summary>
    public ulong ExtraId2 { get; set; }

    /// <summary>
    ///     May be used for some future event.
    /// </summary>
    public string ExtraString { get; set; }
}

public class AwardedUser
{
}