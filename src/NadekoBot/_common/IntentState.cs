namespace NadekoBot.Common;

// Discord closes the gateway with 4014 when the bot asks for an intent which the application does not have.
public sealed record IntentState(bool Presence, bool Members, bool MessageContent)
{
    public static readonly IntentState All = new(true, true, true);
    public static readonly IntentState None = new(false, false, false);

    // Each pair: the intent which Discord approved after a review, and the switch of an app below the review limit.
    private const long PRESENCE_FLAGS = (1L << 12) | (1L << 13);
    private const long MEMBERS_FLAGS = (1L << 14) | (1L << 15);
    private const long MESSAGE_CONTENT_FLAGS = (1L << 18) | (1L << 19);

    // Indexed by MissingMask.
    private static readonly string[] _missingNames =
    [
        "", "Presence", "Server Members", "Presence, Server Members", "Message Content", "Presence, Message Content",
        "Server Members, Message Content", "Presence, Server Members, Message Content"
    ];

    public bool IsAll
        => Presence && Members && MessageContent;

    // Bit 0 Presence, bit 1 Server Members, bit 2 Message Content, each set when the intent is missing.
    public int MissingMask
        => (Presence ? 0 : 1) | (Members ? 0 : 2) | (MessageContent ? 0 : 4);

    public GatewayIntents GatewayIntents
        => GatewayIntents.AllUnprivileged
           | (Presence ? GatewayIntents.GuildPresences : GatewayIntents.None)
           | (Members ? GatewayIntents.GuildMembers : GatewayIntents.None)
           | (MessageContent ? GatewayIntents.MessageContent : GatewayIntents.None);

    public static IntentState FromApplicationFlags(long flags)
        => new((flags & PRESENCE_FLAGS) != 0,
            (flags & MEMBERS_FLAGS) != 0,
            (flags & MESSAGE_CONTENT_FLAGS) != 0);

    public string MissingNames()
        => _missingNames[MissingMask];

    // True when this state misses an intent which the asked state has.
    public bool LacksAnyOf(IntentState asked)
        => (MissingMask & ~asked.MissingMask) != 0;
}
