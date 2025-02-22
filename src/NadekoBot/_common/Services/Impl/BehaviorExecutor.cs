#nullable disable
using Microsoft.Extensions.DependencyInjection;
using NadekoBot.Common.ModuleBehaviors;

namespace NadekoBot.Services;

// should be renamed to handler as it's not only executing
public sealed class BehaviorHandler : IBehaviorHandler 
{
    private readonly IServiceProvider _services;
    
    private IReadOnlyCollection<IExecNoCommand> noCommandExecs;
    private IReadOnlyCollection<IExecPreCommand> preCommandExecs;
    private IReadOnlyCollection<IExecOnMessage> onMessageExecs;
    private IReadOnlyCollection<IInputTransformer> inputTransformers;

    private readonly SemaphoreSlim _customLock = new(1, 1);
    private readonly List<ICustomBehavior> _customExecs = new();

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

    public async Task AddRangeAsync(IEnumerable<ICustomBehavior> execs)
    {
        await _customLock.WaitAsync();
        try
        {
            foreach (var exe in execs)
            {
                if (_customExecs.Contains(exe))
                    continue;

                _customExecs.Add(exe);
            }
        }
        finally
        {
            _customLock.Release();
        }
    }
    
    public async Task<bool> AddAsync(ICustomBehavior behavior)
    {
        await _customLock.WaitAsync();
        try
        {
            if (_customExecs.Contains(behavior))
                return false;

            _customExecs.Add(behavior);
            return true;
        }
        finally
        {
            _customLock.Release();
        }
    }
    
    public async Task<bool> RemoveAsync(ICustomBehavior behavior)
    {
        await _customLock.WaitAsync();
        try
        {
            return _customExecs.Remove(behavior);
        }
        finally
        {
            _customLock.Release();
        }
    }
    
    public async Task RemoveRangeAsync(IEnumerable<ICustomBehavior> behs)
    {
        await _customLock.WaitAsync();
        try
        {
            foreach(var beh in behs)
                _customExecs.Remove(beh);
        }
        finally
        {
            _customLock.Release();
        }
    }

    #endregion
    
    #region Running

    public async Task<bool> RunExecOnMessageAsync(SocketGuild guild, IUserMessage usrMsg)
    {
        async Task<bool> Exec<T>(IReadOnlyCollection<T> execs)
            where T : IExecOnMessage
        {
            foreach (var exec in execs)
            {
                try
                {
                    if (await exec.ExecOnMessageAsync(guild, usrMsg))
                    {
                        Log.Information("{TypeName} blocked message g:{GuildId} u:{UserId} c:{ChannelId} msg:{Message}",
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
                        "An error occurred in {TypeName} late blocker: {ErrorMessage}",
                        GetExecName(exec),
                        ex.Message);
                }
            }

            return false;
        }

        if (await Exec(onMessageExecs))
        {
            return true;
        }

        await _customLock.WaitAsync();
        try
        {
            if (await Exec(_customExecs))
                return true;
        }
        finally
        {
            _customLock.Release();
        }

        return false;
    }

    private string GetExecName(IBehavior exec)
        => exec.Name;

    public async Task<bool> RunPreCommandAsync(ICommandContext ctx, CommandInfo cmd)
    {
        async Task<bool> Exec<T>(IReadOnlyCollection<T> execs) where T: IExecPreCommand
        {
            foreach (var exec in execs)
            {
                try
                {
                    if (await exec.ExecPreCommandAsync(ctx, cmd.Module.GetTopLevelModule().Name, cmd))
                    {
                        Log.Information("{TypeName} Pre-Command blocked [{User}] Command: [{Command}]",
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

        if (await Exec(preCommandExecs))
            return true;

        await _customLock.WaitAsync();
        try
        {
            if (await Exec(_customExecs))
                return true;
        }
        finally
        {
            _customLock.Release();
        }

        return false;
    }

    public async Task RunOnNoCommandAsync(SocketGuild guild, IUserMessage usrMsg)
    {
        async Task Exec<T>(IReadOnlyCollection<T> execs) where T : IExecNoCommand
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
                        GetExecName(exec),
                        ex.Message);
                }
            }
        }

        await Exec(noCommandExecs);
        
        await _customLock.WaitAsync();
        try
        {
            await Exec(_customExecs);
        }
        finally
        {
            _customLock.Release();
        }
    }

    public async Task<string> RunInputTransformersAsync(SocketGuild guild, IUserMessage usrMsg)
    {
        async Task<string> Exec<T>(IReadOnlyCollection<T> execs, string content)
            where T : IInputTransformer
        {
            foreach (var exec in execs)
            {
                try
                {
                    var newContent = await exec.TransformInput(guild, usrMsg.Channel, usrMsg.Author, content);
                    if (newContent is not null)
                    {
                        Log.Information("{ExecName} transformed content {OldContent} -> {NewContent}",
                            GetExecName(exec),
                            content,
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

        var newContent = await Exec(inputTransformers, usrMsg.Content);
        if (newContent is not null)
            return newContent;
        
        await _customLock.WaitAsync();
        try
        {
            newContent = await Exec(_customExecs, usrMsg.Content);
            if (newContent is not null)
                return newContent;
        }
        finally
        {
            _customLock.Release();
        }

        return usrMsg.Content;
    }

    public async ValueTask RunPostCommandAsync(ICommandContext ctx, string moduleName, CommandInfo cmd)
    {
        foreach (var exec in _customExecs)
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