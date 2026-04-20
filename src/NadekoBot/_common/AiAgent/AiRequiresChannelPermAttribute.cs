namespace NadekoBot.AiAgent;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class AiRequiresChannelPermAttribute(ChannelPermission permission) : Attribute
{
    public ChannelPermission Permission { get; } = permission;
}
