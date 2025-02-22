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

            var roles = await _service.ToggleAarAsync(ctx.Guild.Id, role.Id);
            if (roles.Count == 0)
                await Response().Confirm(strs.aar_disabled).SendAsync();
            else if (roles.Contains(role.Id))
                await AutoAssignRole();
            else
                await Response().Confirm(strs.aar_role_removed(Format.Bold(role.ToString()))).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        [BotPerm(GuildPerm.ManageRoles)]
        public async Task AutoAssignRole()
        {
            if (!_service.TryGetRoles(ctx.Guild.Id, out var roles))
            {
                await Response().Confirm(strs.aar_none).SendAsync();
                return;
            }

            var existing = roles.Select(rid => ctx.Guild.GetRole(rid)).Where(r => r is not null).ToList();

            if (existing.Count != roles.Count)
                await _service.SetAarRolesAsync(ctx.Guild.Id, existing.Select(x => x.Id));

            await Response()
                  .Confirm(strs.aar_roles(
                      '\n' + existing.Select(x => Format.Bold(x.ToString())).Join(",\n")))
                  .SendAsync();
        }
    }
}