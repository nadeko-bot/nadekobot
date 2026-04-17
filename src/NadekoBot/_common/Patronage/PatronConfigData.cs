using NadekoBot.Common.Yml;

namespace NadekoBot.Modules.Patronage;

public sealed class PatronConfigData
{
    [Comment("DO NOT CHANGE THE VERSION MANUALLY")]
    public int Version { get; set; } = 3;

    [Comment("Whether the patronage feature is enabled")]
    public bool IsEnabled { get; set; }
    
    [Comment("Quotas for patron system")]
    public Dictionary<PatronTier, Dictionary<string, int>> Quotas { get; set; } = new();
}