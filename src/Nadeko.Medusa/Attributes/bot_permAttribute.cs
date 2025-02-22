using Discord;

namespace NadekoBot.Medusa;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class bot_permAttribute : MedusaPermAttribute
{
    public GuildPermission? GuildPerm { get; }
    public ChannelPermission? ChannelPerm { get; }


    public bot_permAttribute(GuildPermission perm)
    {
        GuildPerm = perm;
        ChannelPerm = null;
    }

    public bot_permAttribute(ChannelPermission perm)
    {
        ChannelPerm = perm;
        GuildPerm = null;
    }
}