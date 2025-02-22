namespace NadekoBot.Medusa;

/// <summary>
/// Sets the priority of a command in case there are multiple commands with the same name but different parameters.
/// Higher value means higher priority.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class prioAttribute : Attribute
{
    public int Priority { get; }

    /// <summary>
    /// Snek command priority
    /// </summary>
    /// <param name="priority">Priority value. The higher the value, the higher the priority</param>
    public prioAttribute(int priority)
    {
        Priority = priority;
    }
}