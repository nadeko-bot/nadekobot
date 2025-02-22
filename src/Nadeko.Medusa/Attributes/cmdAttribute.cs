namespace NadekoBot.Medusa;

/// <summary>
/// Marks a method as a snek command
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class cmdAttribute : Attribute
{
    /// <summary>
    /// Command description. Avoid using, as cmds.yml is preferred
    /// </summary>
    public string? desc { get; set; }
    
    /// <summary>
    /// Command args examples. Avoid using, as cmds.yml is preferred
    /// </summary>
    public string[]? args { get; set; }
    
    /// <summary>
    /// Command aliases
    /// </summary>
    public string[] Aliases { get; }

    public cmdAttribute()
    {
        desc = null;
        args = null;
        Aliases = Array.Empty<string>();
    }
    
    public cmdAttribute(params string[] aliases)
    {
        Aliases = aliases;
        desc = null;
        args = null;
    }
}