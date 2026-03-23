using NadekoBot.Common.Yml;

namespace NadekoBot.Modules.Music;

[Cloneable]
public sealed partial class AudioFileCacheConfig : ICloneable<AudioFileCacheConfig>
{
    [Comment("DO NOT CHANGE")]
    public int Version { get; set; } = 1;

    [Comment("Maximum total cache size in gigabytes. Minimum 1. Default 20")]
    public int MaxCacheSizeGb { get; set; } = 20;
}
