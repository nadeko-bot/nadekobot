using Discord;

namespace NadekoBot.Medusa;

/// <summary>
/// Commands which take this type as a first parameter can only be executed in a server
/// </summary>
public abstract class GuildContext : AnyContext
{
   public abstract override ITextChannel Channel { get; }
   public abstract IGuild Guild { get; }
}