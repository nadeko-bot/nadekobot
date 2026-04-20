namespace NadekoBot.AiAgent;

[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class AiParamAttribute(string description) : Attribute
{
    public string Description { get; } = description;
}
