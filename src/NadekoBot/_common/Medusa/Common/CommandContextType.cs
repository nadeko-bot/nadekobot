namespace NadekoBot.Medusa;

/// <summary>
/// Enum specifying in which context the command can be executed
/// </summary>
public enum CommandContextType
{
    /// <summary>
    /// Command can only be executed in a guild
    /// </summary>
    Guild,
    
    /// <summary>
    /// Command can only be executed in DMs
    /// </summary>
    Dm,
    
    /// <summary>
    /// Command can be executed anywhere
    /// </summary>
    Any,
    
    /// <summary>
    /// Command can be executed anywhere, and it doesn't require context to be passed to it
    /// </summary>
    Unspecified
}