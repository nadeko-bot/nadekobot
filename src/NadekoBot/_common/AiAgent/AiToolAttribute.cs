namespace NadekoBot.AiAgent;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AiToolAttribute(string name, string description) : Attribute
{
    public string Name { get; } = name;
    public string Description { get; } = description;
}
