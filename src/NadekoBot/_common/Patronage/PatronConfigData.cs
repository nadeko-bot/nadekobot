using NadekoBot.Common.Yml;
using Cloneable;

namespace NadekoBot.Modules.Patronage;

[Cloneable]
public partial class PatronConfigData : ICloneable<PatronConfigData>
{
    [Comment("DO NOT CHANGE")]
    public int Version { get; set; } = 3;

    [Comment("Whether the patronage feature is enabled")]
    public bool IsEnabled { get; set; }

    [Comment("Who can do how much of what")]
    public Dictionary<int, Dictionary<LimitedFeatureName, QuotaLimit>> Limits { get; set; } = new();
}