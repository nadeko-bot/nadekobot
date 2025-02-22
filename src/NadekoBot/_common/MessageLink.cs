namespace NadekoBot.Modules.Administration.Services;

public sealed record class MessageLink(IGuild? Guild, IChannel Channel, IMessage Message)
{
    public override string ToString()
        => $"https://discord.com/channels/{(Guild?.Id.ToString() ?? "@me")}/{Channel.Id}/{Message.Id}";
}