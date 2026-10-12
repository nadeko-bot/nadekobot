using NadekoBot.Common.Yml;

namespace NadekoBot.Modules.Music;

public sealed class AudioFileCacheConfig
{
    [Comment("DO NOT CHANGE THE VERSION MANUALLY")]
    public int Version { get; set; } = 2;

    [Comment("Maximum total cache size in gigabytes. Minimum 1. Default 10")]
    public int MaxCacheSizeGb { get; set; } = 10;

    [Comment("Maximum number of tracks in the queue of one server. Default 500")]
    public int MaxQueueSize { get; set; } = 500;
}
