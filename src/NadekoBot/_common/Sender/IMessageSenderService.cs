namespace NadekoBot.Extensions;

public interface IMessageSenderService
{
    ResponseBuilder Response(IMessageChannel channel);
    ResponseBuilder Response(ICommandContext ctx);
    ResponseBuilder Response(IUser user);

    ResponseBuilder Response(SocketMessageComponent smc);

    NadekoEmbedBuilder CreateEmbed(ulong? guildId = null);
}