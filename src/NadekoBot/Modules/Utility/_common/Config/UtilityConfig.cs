using NadekoBot.Common.Yml;

namespace NadekoBot.Modules.Utility;

public sealed class UtilityConfig
{
    [Comment("DO NOT CHANGE")]
    public int Version { get; set; } = 1;

    [Comment("Maximum number of repeating messages per server. Default 5")]
    public int MaxRepeaters { get; set; } = 5;

    [Comment("Maximum number of scheduled commands per user per server. Default 5")]
    public int MaxScheduledPerUser { get; set; } = 5;

    [Comment("Default maximum number of live channels per server. Default 5")]
    public int MaxLiveChannels { get; set; } = 5;
}
