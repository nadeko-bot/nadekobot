namespace NadekoBot.Services;

public interface IBehaviorHandler
{
    Task<bool> AddAsync(ICustomBehavior behavior);
    Task AddRangeAsync(IEnumerable<ICustomBehavior> behavior);
    Task<bool> RemoveAsync(ICustomBehavior behavior);
    Task RemoveRangeAsync(IEnumerable<ICustomBehavior> behs);
    
    ValueTask<bool> RunExecOnMessageAsync(SocketGuild? guild, IUserMessage usrMsg);
    ValueTask<string> RunInputTransformersAsync(SocketGuild? guild, IUserMessage usrMsg);
    ValueTask<bool> RunPreCommandAsync(ICommandContext context, CommandInfo cmd);
    ValueTask RunPostCommandAsync(ICommandContext ctx, string moduleName, CommandInfo cmd);
    ValueTask RunOnNoCommandAsync(SocketGuild? guild, IUserMessage usrMsg);
    void Initialize();
}