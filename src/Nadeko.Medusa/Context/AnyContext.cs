using Discord;

namespace NadekoBot.Medusa;

/// <summary>
/// Commands which take this class as a first parameter can be executed in both DMs and Servers 
/// </summary>
public abstract class AnyContext
{
    /// <summary>
    /// Channel from the which the command is invoked
    /// </summary>
    public abstract IMessageChannel Channel { get; }
    
    /// <summary>
    /// Message which triggered the command
    /// </summary>
    public abstract IUserMessage Message { get; }
    
    /// <summary>
    /// The user who invoked the command
    /// </summary>
    public abstract IUser User { get; }
    
    /// <summary>
    /// Bot user
    /// </summary>
    public abstract ISelfUser Bot { get; }

    /// <summary>
    /// Provides access to strings used by this medusa
    /// </summary>
    public abstract IMedusaStrings Strings { get; } 
    
    /// <summary>
    /// Gets a formatted localized string using a key and arguments which should be formatted in
    /// </summary>
    /// <param name="key">The key of the string as specified in localization files</param>
    /// <param name="args">Arguments (if any) to format in</param>
    /// <returns>A formatted localized string</returns>
    public abstract string GetText(string key, object[]? args = null);
}