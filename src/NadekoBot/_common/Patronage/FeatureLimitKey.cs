namespace NadekoBot.Modules.Patronage;

public enum LimitedFeatureName
{
    ChatBot,
    ReactionRole,
    Prune,
    
}
public readonly struct FeatureLimitKey
{
    public string PrettyName { get; init; }
    public string Key { get; init; }
}