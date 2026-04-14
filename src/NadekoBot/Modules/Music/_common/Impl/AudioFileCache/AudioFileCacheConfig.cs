using NadekoBot.Common.Yml;

namespace NadekoBot.Modules.Music;

public sealed class AudioFileCacheConfig
{
    [Comment("DO NOT CHANGE")]
    public int Version { get; set; } = 1;

    [Comment("Maximum total cache size in gigabytes. Minimum 1. Default 10")]
    public int MaxCacheSizeGb { get; set; } = 10;
}
