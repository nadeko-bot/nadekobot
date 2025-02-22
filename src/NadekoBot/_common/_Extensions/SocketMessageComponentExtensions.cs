namespace NadekoBot.Extensions;

public static class SocketMessageComponentExtensions
{
    public static async Task RespondAsync(
        this SocketMessageComponent ch,
        IMessageSenderService sender,
        string text,
        MsgType type,
        bool ephemeral = false)
    {
        var embed = sender.CreateEmbed(ch.GuildId).WithDescription(text);

        embed = (type switch
        {
            MsgType.Error => embed.WithErrorColor(),
            MsgType.Ok => embed.WithOkColor(),
            MsgType.Pending => embed.WithPendingColor(),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        });

        await ch.RespondAsync(embeds: [embed.Build()], ephemeral: ephemeral);
    }

    // embed title and optional footer overloads

    public static Task RespondConfirmAsync(
        this SocketMessageComponent smc,
        IMessageSenderService sender,
        string text,
        bool ephemeral = false)
        => smc.RespondAsync(sender, text, MsgType.Ok, ephemeral);
    
}