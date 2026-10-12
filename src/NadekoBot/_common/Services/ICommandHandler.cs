namespace NadekoBot.Services;

public interface ICommandHandler
{
    event Func<IUserMessage, CommandInfo, Task> CommandExecuted;
    event Func<CommandInfo, ITextChannel?, string, Task> CommandErrored;

    string GetPrefix(IGuild? ctxGuild);
    string GetPrefix(ulong? id = null);
    string SetDefaultPrefix(string toSet);
    string SetPrefix(IGuild ctxGuild, string toSet);

    Task TryRunCommand(SocketGuild? guild, ISocketMessageChannel channel, IUserMessage usrMsg);
}