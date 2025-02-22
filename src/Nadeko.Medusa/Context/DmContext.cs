using Discord;

namespace NadekoBot.Medusa;

/// <summary>
/// Commands which take this type as the first parameter can only be executed in DMs
/// </summary>
public abstract class DmContext : AnyContext
{
    public abstract override IDMChannel Channel { get; }
}