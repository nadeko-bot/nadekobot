#nullable disable
using NadekoBot.Modules.Permissions.Services;

namespace NadekoBot.Modules.Permissions;

public partial class Permissions
{
    [Group]
    public partial class ResetPermissionsCommands : NadekoModule
    {
        private readonly GlobalPermissionService _gps;
        private readonly PermissionService _perms;

        public ResetPermissionsCommands(GlobalPermissionService gps, PermissionService perms)
        {
            _gps = gps;
            _perms = perms;
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task ResetPerms()
        {
            await _perms.Reset(ctx.Guild.Id);
            await Response().Confirm(strs.perms_reset).SendAsync();
        }

        [Cmd]
        [OwnerOnly]
        public async Task ResetGlobalPerms()
        {
            await _gps.Reset();
            await Response().Confirm(strs.global_perms_reset).SendAsync();
        }
    }
}