using NadekoBot.Modules.Permissions.Common;
using NadekoBot.Modules.Permissions.Services;

namespace NadekoBot;

public sealed class PermissionChecker : IPermissionChecker, INService
{
    private readonly PermissionService _perms;
    private readonly GlobalPermissionService _gperm;
    private readonly CmdCdService _cmdCds;
    private readonly IMessageSenderService _sender;
    private readonly CommandHandler _ch;

    public PermissionChecker(
        PermissionService perms,
        GlobalPermissionService gperm,
        CmdCdService cmdCds,
        IMessageSenderService sender,
        CommandHandler ch)
    {
        _perms = perms;
        _gperm = gperm;
        _cmdCds = cmdCds;
        _sender = sender;
        _ch = ch;
    }

    public async Task<PermCheckResult> CheckPermsAsync(
        IGuild guild,
        IMessageChannel channel,
        IUser author,
        string module,
        string? cmdName)
    {
        module = module.ToLowerInvariant();
        cmdName = cmdName?.ToLowerInvariant();

        if (cmdName is not null && await _cmdCds.TryBlock(guild, author, cmdName))
        {
            return new PermCooldown();
        }

        try
        {
            if (_gperm.BlockedModules.Contains(module))
            {
                return new PermGlobalBlock();
            }

            if (cmdName is not null && _gperm.BlockedCommands.Contains(cmdName))
            {
                return new PermGlobalBlock();
            }

            var pc = _perms.GetCacheFor(guild.Id);
            if (!pc.Permissions.CheckPermissions(author, channel, cmdName, module, out var index))
            {
                return new PermDisallowed(index,
                    pc.Permissions[index].GetCommand(_ch.GetPrefix(guild), guild as SocketGuild),
                    pc.Verbose);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Permission check failed for {Module}/{Command}", module, cmdName);
            return new PermGlobalBlock();
        }

        return new PermAllowed();
    }
}