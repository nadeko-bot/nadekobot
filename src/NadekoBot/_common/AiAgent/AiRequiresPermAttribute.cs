namespace NadekoBot.AiAgent;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class AiRequiresPermAttribute(GuildPermission permission) : Attribute
{
    public GuildPermission Permission { get; } = permission;
}
