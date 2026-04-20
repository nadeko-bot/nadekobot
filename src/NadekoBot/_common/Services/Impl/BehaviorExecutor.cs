using Microsoft.Extensions.DependencyInjection;
using NadekoBot.Common.ModuleBehaviors;

namespace NadekoBot.Services;

// should be renamed to handler as it's not only executing
public sealed class BehaviorHandler : IBehaviorHandler 
{
    private readonly IServiceProvider _services;
    
    private IExecNoCommand[] noCommandExecs = [];
    private IExecPreCommand[] preCommandExecs = [];
    private IExecOnMessage[] onMessageExecs = [];
    private IInputTransformer[] inputTransformers = [];

    private readonly Lock _customLock = new();
    private volatile ICustomBehavior[] _customExecs = [];

    public BehaviorHandler(IServiceProvider services)
    {
        _services = services;
    }

    public void Initialize()
    {
        noCommandExecs = _services.GetServices<IExecNoCommand>().ToArray();
        preCommandExecs = _services.GetServices<IExecPreCommand>().OrderByDescending(x => x.Priority).ToArray();
        onMessageExecs = _services.GetServices<IExecOnMessage>().OrderByDescending(x => x.Priority).ToArray();
        inputTransformers = _services.GetServices<IInputTransformer>().ToArray();
    }

    #region Add/Remove

    public Task AddRangeAsync(IEnumerable<ICustomBehavior> execs)
    {
        lock (_customLock)
        {
            var list = new List<ICustomBehavior>(_customExecs);
            foreach (var exe in execs)
            {
                if (!list.Contains(exe))
                    list.Add(exe);
            }

            _customExecs = list.ToArray();
        }

        return Task.CompletedTask;
    }
    
    public Task<bool> AddAsync(ICustomBehavior behavior)
    {
        lock (_customLock)
        {
            var snapshot = _customExecs;
            if (Array.IndexOf(snapshot, behavior) >= 0)
                return Task.FromResult(false);

            var newArr = new ICustomBehavior[snapshot.Length + 1];
            snapshot.CopyTo(newArr, 0);
            newArr[snapshot.Length] = behavior;
            _customExecs = newArr;
        }

        return Task.FromResult(true);
    }
    
    public Task<bool> RemoveAsync(ICustomBehavior behavior)
    {
        lock (_customLock)
        {
            var snapshot = _customExecs;
            var idx = Array.IndexOf(snapshot, behavior);
            if (idx < 0)
                return Task.FromResult(false);

            var newArr = new ICustomBehavior[snapshot.Length - 1];
            Array.Copy(snapshot, 0, newArr, 0, idx);
            Array.Copy(snapshot, idx + 1, newArr, idx, snapshot.Length - idx - 1);
            _customExecs = newArr;
        }

        return Task.FromResult(true);
    }
    
    public Task RemoveRangeAsync(IEnumerable<ICustomBehavior> behs)
    {
        lock (_customLock)
        {
            var list = new List<ICustomBehavior>(_customExecs);
            foreach (var beh in behs)
                list.Remove(beh);

            _customExecs = list.ToArray();
        }

        return Task.CompletedTask;
    }

    #endregion
    
    #region Running

    public async ValueTask<bool> RunExecOnMessageAsync(SocketGuild? guild, IUserMessage usrMsg)
    {
        if (await ExecOnMessageInternalAsync(onMessageExecs, guild, usrMsg))
            return true;

        var customs = _customExecs;
        if (customs.Length > 0 && await ExecOnMessageInternalAsync(customs, guild, usrMsg))
            return true;

        return false;
    }

    private async ValueTask<bool> ExecOnMessageInternalAsync<T>(T[] execs, SocketGuild? guild, IUserMessage usrMsg)
        where T : IExecOnMessage
    {
        foreach (var exec in execs)
        {
            try
            {
                if (await exec.ExecOnMessageAsync(guild, usrMsg))
                {
                    Log.Information("{TypeName} intercepted message g:{GuildId} u:{UserId} c:{ChannelId} msg:{Message}",
                        GetExecName(exec),
                        guild?.Id,
                        usrMsg.Author.Id,
                        usrMsg.Channel.Id,
                        usrMsg.Content?.TrimTo(10));
                    
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex,
                    "An error occurred in {TypeName} OnMessage handler: {ErrorMessage}",
                    GetExecName(exec),
                    ex.Message);
            }
        }

        return false;
    }

    private string GetExecName(IBehavior exec)
        => exec.Name;

    public async ValueTask<bool> RunPreCommandAsync(ICommandContext ctx, CommandInfo cmd)
    {
        if (await ExecPreCommandInternalAsync(preCommandExecs, ctx, cmd))
            return true;

        var customs = _customExecs;
        if (customs.Length > 0 && await ExecPreCommandInternalAsync(customs, ctx, cmd))
            return true;

        return false;
    }

    private async ValueTask<bool> ExecPreCommandInternalAsync<T>(T[] execs, ICommandContext ctx, CommandInfo cmd)
        where T : IExecPreCommand
    {
        foreach (var exec in execs)
        {
            try
            {
                if (await exec.ExecPreCommandAsync(ctx, cmd.Module.GetTopLevelModule().Name, cmd))
                {
                    Log.Information("{TypeName} intercepted [{User}] Command: [{Command}]",
                        GetExecName(exec),
                        ctx.User,
                        cmd.Aliases[0]);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex,
                    "An error occurred in {TypeName} PreCommand: {ErrorMessage}",
                    GetExecName(exec),
                    ex.Message);
            }
        }

        return false;
    }

    public async ValueTask RunOnNoCommandAsync(SocketGuild? guild, IUserMessage usrMsg)
    {
        await ExecNoCommandInternalAsync(noCommandExecs, guild, usrMsg);

        var customs = _customExecs;
        if (customs.Length > 0)
            await ExecNoCommandInternalAsync(customs, guild, usrMsg);
    }

    private static async ValueTask ExecNoCommandInternalAsync<T>(T[] execs, SocketGuild? guild, IUserMessage usrMsg)
        where T : IExecNoCommand
    {
        foreach (var exec in execs)
        {
            try
            {
                await exec.ExecOnNoCommandAsync(guild, usrMsg);
            }
            catch (Exception ex)
            {
                Log.Error(ex,
                    "An error occurred in {TypeName} OnNoCommand: {ErrorMessage}",
                    exec.Name,
                    ex.Message);
            }
        }
    }

    public async ValueTask<string> RunInputTransformersAsync(SocketGuild? guild, IUserMessage usrMsg)
    {
        var newContent = await ExecInputTransformInternalAsync(inputTransformers, guild, usrMsg);
        if (newContent is not null)
            return newContent;
        
        var customs = _customExecs;
        if (customs.Length > 0)
        {
            newContent = await ExecInputTransformInternalAsync(customs, guild, usrMsg);
            if (newContent is not null)
                return newContent;
        }

        return usrMsg.Content;
    }

    private async ValueTask<string?> ExecInputTransformInternalAsync<T>(T[] execs, SocketGuild? guild, IUserMessage usrMsg)
        where T : IInputTransformer
    {
        foreach (var exec in execs)
        {
            try
            {
                var newContent = await exec.TransformInput(guild, usrMsg.Channel, usrMsg.Author, usrMsg.Content);
                if (newContent is not null)
                {
                    Log.Information("{ExecName} transformed content {OldContent} -> {NewContent}",
                        GetExecName(exec),
                        usrMsg.Content,
                        newContent);
                    return newContent;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "An error occured during InputTransform handling: {ErrorMessage}", ex.Message);
            }
        }

        return null;
    }

    public async ValueTask RunPostCommandAsync(ICommandContext ctx, string moduleName, CommandInfo cmd)
    {
        var customs = _customExecs;
        foreach (var exec in customs)
        {
            try
            {
                await exec.ExecPostCommandAsync(ctx, moduleName, cmd.Name);
            }
            catch (Exception ex)
            {
                Log.Warning(ex,
                    "An error occured during PostCommand handling in {ExecName}: {ErrorMessage}",
                    GetExecName(exec),
                    ex.Message);
            }
        }
    }
    
    #endregion
}