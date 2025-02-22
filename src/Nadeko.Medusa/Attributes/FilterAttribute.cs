namespace NadekoBot.Medusa;

/// <summary>
/// Overridden to implement custom checks which commands have to pass in order to be executed.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public abstract class FilterAttribute : Attribute
{
    public abstract ValueTask<bool> CheckAsync(AnyContext ctx);
}