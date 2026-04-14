#nullable disable
using NadekoBot.Common.ModuleBehaviors;
using Discord;
using NadekoBot.Db;

namespace NadekoBot.Modules.Permissions.Services;

public class GlobalPermissionService : IExecPreCommand, INService
{
    public int Priority { get; } = 0;

    public HashSet<string> BlockedCommands
        => _bss.Data.Blocked.Commands;

    public HashSet<string> BlockedModules
        => _bss.Data.Blocked.Modules;

    private readonly BotConfigService _bss;
    private readonly IBotCredsProvider _creds;

    public GlobalPermissionService(BotConfigService bss, IBotCredsProvider creds)
    {
        _bss = bss;
        _creds = creds;
    }


    public Task<bool> ExecPreCommandAsync(ICommandContext ctx, string moduleName, CommandInfo command)
    {
        var settings = _bss.Data;
        var commandName = command.Name.ToLowerInvariant();

        if (commandName != "resetglobalperms")
        {
            if (settings.Blocked.Commands.Contains(commandName)
                || settings.Blocked.Modules.Contains(moduleName.ToLowerInvariant()))
                return Task.FromResult(true);

            if (ctx.Guild is null)
            {
                if (settings.DmBlocked.Commands.Contains(commandName)
                    || settings.DmBlocked.Modules.Contains(moduleName.ToLowerInvariant()))
                    return Task.FromResult(true);
            }
            
            if (settings.CommandOverrides.TryGetValue(commandName, out var overrideStr))
            {
                if (overrideStr.Equals("bot owner only", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_creds.GetCreds().IsOwner(ctx.User))
                        return Task.FromResult(true);
                }
                else if (Enum.TryParse<NadekoBot.Db.GuildPerm>(overrideStr, true, out var perm))
                {
                    if (ctx.User is not IGuildUser gu || !gu.GuildPermissions.Has((GuildPermission)perm))
                        return Task.FromResult(true);
                }
            }
        }

        return Task.FromResult(false);
    }
    
    public void SetOverride(string commandName, string overrideStr)
    {
        _bss.ModifyConfig(bs =>
        {
            bs.CommandOverrides[commandName.ToLowerInvariant()] = overrideStr;
        });
    }

    public bool ClearOverride(string commandName)
    {
        var removed = false;
        _bss.ModifyConfig(bs =>
        {
            removed = bs.CommandOverrides.Remove(commandName.ToLowerInvariant());
        });
        return removed;
    }

    public void ClearAllOverrides()
    {
        _bss.ModifyConfig(bs =>
        {
            bs.CommandOverrides.Clear();
        });
    }

    /// <summary>
    ///     Toggles module blacklist
    /// </summary>
    /// <param name="moduleName">Lowercase module name</param>
    /// <returns>Whether the module is added</returns>
    public bool ToggleModule(string moduleName, bool priv = false)
    {
        var added = false;
        _bss.ModifyConfig(bs =>
        {
            if (priv)
            {
                if (bs.DmBlocked.Modules.Add(moduleName))
                {
                    added = true;
                }
                else
                {
                    bs.DmBlocked.Modules.Remove(moduleName);
                    added = false;
                }

                return;
            }

            if (bs.Blocked.Modules.Add(moduleName))
            {
                added = true;
            }
            else
            {
                bs.Blocked.Modules.Remove(moduleName);
                added = false;
            }
        });

        return added;
    }

    /// <summary>
    ///     Toggles command blacklist
    /// </summary>
    /// <param name="commandName">Lowercase command name</param>
    /// <returns>Whether the command is added</returns>
    public bool ToggleCommand(string commandName, bool priv = false)
    {
        var added = false;
        _bss.ModifyConfig(bs =>
        {
            if (priv)
            {
                if (bs.DmBlocked.Commands.Add(commandName))
                {
                    added = true;
                }
                else
                {
                    bs.DmBlocked.Commands.Remove(commandName);
                    added = false;
                }

                return;
            }

            if (bs.Blocked.Commands.Add(commandName))
            {
                added = true;
            }
            else
            {
                bs.Blocked.Commands.Remove(commandName);
                added = false;
            }
        });

        return added;
    }

    /// <summary>
    ///     Resets all global permissions
    /// </summary>
    public Task Reset()
    {
        _bss.ModifyConfig(bs =>
        {
            bs.Blocked.Commands.Clear();
            bs.Blocked.Modules.Clear();
        });

        return Task.CompletedTask;
    }
}