#nullable disable
namespace NadekoBot.Services;

public interface IBehaviorHandler
{
    Task<bool> AddAsync(ICustomBehavior behavior);
    Task AddRangeAsync(IEnumerable<ICustomBehavior> behavior);
    Task<bool> RemoveAsync(ICustomBehavior behavior);
    Task RemoveRangeAsync(IEnumerable<ICustomBehavior> behs);
    
    Task<bool> RunExecOnMessageAsync(SocketGuild guild, IUserMessage usrMsg);
    Task<string> RunInputTransformersAsync(SocketGuild guild, IUserMessage usrMsg);
    Task<bool> RunPreCommandAsync(ICommandContext context, CommandInfo cmd);
    ValueTask RunPostCommandAsync(ICommandContext ctx, string moduleName, CommandInfo cmd);
    Task RunOnNoCommandAsync(SocketGuild guild, IUserMessage usrMsg);
    void Initialize();
}