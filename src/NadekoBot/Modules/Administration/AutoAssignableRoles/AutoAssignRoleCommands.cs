#nullable disable
using NadekoBot.Modules.Administration.Services;

namespace NadekoBot.Modules.Administration;

public partial class Administration
{
    [Group]
    public partial class AutoAssignRoleCommands : NadekoModule<AutoAssignRoleService>
    {
        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        [BotPerm(GuildPerm.ManageRoles)]
        public async Task AutoAssignRole([Leftover] IRole role)
        {
            var guser = (IGuildUser)ctx.User;
            if (role.Id == ctx.Guild.EveryoneRole.Id)
                return;

            // the user can't aar the role which is higher or equal to his highest role
            if (ctx.User.Id != guser.Guild.OwnerId && guser.GetRoles().Max(x => x.Position) <= role.Position)
            {
                await Response().Error(strs.hierarchy).SendAsync();
                return;
            }
            
            // the user can't aar the role which is greater or equal to the bot's highest role
            if (role.Position >= ((SocketGuild)ctx.Guild).CurrentUser.GetRoles().Max(x => x.Position))
            {
                await Response().Error(strs.hierarchy).SendAsync();
                return;
            }

            // roles deleted while the bot was offline would otherwise count toward the limit
            await GetExistingRolesAsync();

            var (result, _) = await _service.ToggleAarAsync(ctx.Guild.Id, role.Id);
            switch (result)
            {
                case AarToggleResult.Disabled:
                    await Response().Confirm(strs.aar_disabled).SendAsync();
                    break;
                case AarToggleResult.Added:
                    await AutoAssignRole();
                    break;
                case AarToggleResult.LimitReached:
                    await Response().Error(strs.aar_limit(AutoAssignRoleService.MAX_ROLES)).SendAsync();
                    break;
                default:
                    await Response().Confirm(strs.aar_role_removed(Format.Bold(role.ToString()))).SendAsync();
                    break;
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        [BotPerm(GuildPerm.ManageRoles)]
        public async Task AutoAssignRole()
        {
            var existing = await GetExistingRolesAsync();
            if (existing.Count == 0)
            {
                await Response().Confirm(strs.aar_none).SendAsync();
                return;
            }

            await Response()
                  .Confirm(strs.aar_roles(
                      '\n' + existing.Select(x => Format.Bold(x.ToString())).Join(",\n")))
                  .SendAsync();
        }

        private async Task<List<IRole>> GetExistingRolesAsync()
        {
            if (!_service.TryGetRoles(ctx.Guild.Id, out var roles))
                return [];

            var existing = new List<IRole>(roles.Count);
            foreach (var roleId in roles)
            {
                if (ctx.Guild.GetRole(roleId) is { } role)
                    existing.Add(role);
            }

            if (existing.Count != roles.Count)
                await _service.SetAarRolesAsync(ctx.Guild.Id, existing.Select(x => x.Id).ToList());

            return existing;
        }
    }
}