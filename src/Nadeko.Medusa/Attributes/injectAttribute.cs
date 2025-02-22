namespace NadekoBot.Medusa;

/// <summary>
/// Marks services in command arguments for injection.
/// The injected services must come after the context and before any input parameters.
/// </summary>
public class injectAttribute : Attribute
{
    
}