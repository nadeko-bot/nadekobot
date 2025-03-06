namespace NadekoBot.Services;

public interface ICommandHandler
{
    string GetPrefix(IGuild ctxGuild);
    string GetPrefix(ulong? id = null);
    string SetDefaultPrefix(string toSet);
    string SetPrefix(IGuild ctxGuild, string toSet);

    Task TryRunCommand(SocketGuild guild, ISocketMessageChannel channel, IUserMessage usrMsg);
}