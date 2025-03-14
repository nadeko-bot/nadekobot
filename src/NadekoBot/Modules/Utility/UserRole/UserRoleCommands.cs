using NadekoBot.Modules.Utility.UserRole;
using SixLabors.ImageSharp.PixelFormats;

namespace NadekoBot.Modules.Utility;

public partial class Utility
{
    [Group]
    public class UserRoleCommands : NadekoModule
    {
        private readonly IUserRoleService _urs;

        public UserRoleCommands(IUserRoleService userRoleService)
        {
            _urs = userRoleService;
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        public async Task UserRoleAssign(IGuildUser user, IRole role)
        {
            var modUser = (IGuildUser)ctx.User;

            if (modUser.GetRoles().Max(x => x.Position) <= role.Position)
            {
                await Response().Error(strs.userrole_hierarchy_error).SendAsync();
                return;
            }
            
            var botUser = ((SocketGuild)ctx.Guild).CurrentUser;

            if (botUser.GetRoles().Max(x => x.Position) <= role.Position)
            {
                await Response().Error(strs.hierarchy).SendAsync();
                return;
            }

            var success = await _urs.AddRoleAsync(ctx.Guild.Id, user.Id, role.Id);

            if (!success)
                return;

            await Response()
                .Confirm(strs.userrole_assigned(Format.Bold(user.ToString()), Format.Bold(role.Name)))
                .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        public async Task UserRoleRemove(IUser user, IRole role)
            => await UserRoleRemove(user, role.Id);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        public async Task UserRoleRemove(IUser user, ulong roleId)
        {
            var role = ctx.Guild.GetRole(roleId);

            var success = await _urs.RemoveRoleAsync(ctx.Guild.Id, user.Id, roleId);
            if (!success)
            {
                await Response().Error(strs.userrole_not_found).SendAsync();
                return;
            }

            await Response()
                .Confirm(strs.userrole_removed(
                    Format.Bold(user.ToString()),
                    Format.Bold(role?.Name ?? roleId.ToString())))
                .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        public async Task UserRoleList()
        {
            var roles = await _urs.ListRolesAsync(ctx.Guild.Id);

            if (roles.Count == 0)
            {
                await Response().Error(strs.userrole_none).SendAsync();
                return;
            }

            var guild = ctx.Guild as SocketGuild;

            // Group roles by user
            var userGroups = roles.GroupBy(r => r.UserId)
                .Select(g => (UserId: g.Key,
                    UserName: guild?.GetUser(g.Key)?.ToString() ?? g.Key.ToString(),
                    Roles: g.ToList()))
                .ToList();

            await Response()
                .Paginated()
                .Items(userGroups)
                .PageSize(5)
                .Page((pageUsers, _) =>
                {
                    var eb = CreateEmbed()
                        .WithTitle(GetText(strs.userrole_list_title))
                        .WithOkColor();

                    foreach (var user in pageUsers)
                    {
                        var roleNames = user.Roles
                            .Select(r => $"- {guild?.GetRole(r.RoleId)} `{r.RoleId}`")
                            .Join("\n");
                        eb.AddField(user.UserName, roleNames);
                    }

                    return eb;
                })
                .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        [BotPerm(GuildPerm.ManageRoles)]
        public async Task UserRoleList(IUser user)
        {
            var roles = await _urs.ListUserRolesAsync(ctx.Guild.Id, user.Id);

            if (roles.Count == 0)
            {
                await Response()
                    .Error(strs.userrole_none_user(Format.Bold(user.ToString())))
                    .SendAsync();
                return;
            }

            var guild = ctx.Guild as SocketGuild;

            await Response()
                .Paginated()
                .Items(roles)
                .PageSize(10)
                .Page((pageRoles, _) =>
                {
                    var roleList = pageRoles
                        .Select(r => $"- {guild?.GetRole(r.RoleId)} `{r.RoleId}`")
                        .Join("\n");

                    return CreateEmbed()
                        .WithTitle(GetText(strs.userrole_list_for_user(Format.Bold(user.ToString()))))
                        .WithDescription(roleList)
                        .WithOkColor();
                })
                .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task UserRoleMy()
            => await UserRoleList(ctx.User);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [BotPerm(GuildPerm.ManageRoles)]
        public async Task UserRoleColor(IRole role, Rgba32 color)
        {
            if (!await _urs.UserOwnsRoleAsync(ctx.Guild.Id, ctx.User.Id, role.Id))
            {
                await Response().Error(strs.userrole_no_permission).SendAsync();
                return;
            }

            var success = await _urs.SetRoleColorAsync(
                ctx.Guild.Id,
                ctx.User.Id,
                role.Id,
                color
            );

            if (success)
            {
                await Response().Confirm(strs.userrole_color_success(Format.Bold(role.Name), color)).SendAsync();
            }
            else
            {
                await Response().Error(strs.userrole_color_fail).SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [BotPerm(GuildPerm.ManageRoles)]
        public async Task UserRoleName(IRole role, [Leftover] string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
            {
                await Response().Error(strs.userrole_name_invalid).SendAsync();
                return;
            }

            if (!await _urs.UserOwnsRoleAsync(ctx.Guild.Id, ctx.User.Id, role.Id))
            {
                await Response().Error(strs.userrole_no_permission).SendAsync();
                return;
            }

            var success = await _urs.SetRoleNameAsync(
                ctx.Guild.Id,
                ctx.User.Id,
                role.Id,
                name
            );

            if (success)
            {
                await Response().Confirm(strs.userrole_name_success(Format.Bold(name))).SendAsync();
            }
            else
            {
                await Response().Error(strs.userrole_name_fail).SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [BotPerm(GuildPerm.ManageRoles)]
        public Task UserRoleIcon(IRole role, Emote emote)
            => UserRoleIcon(role, emote.Url);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [BotPerm(GuildPerm.ManageRoles)]
        public async Task UserRoleIcon(IRole role, [Leftover] string icon)
        {
            if (string.IsNullOrWhiteSpace(icon))
            {
                await Response().Error(strs.userrole_icon_invalid).SendAsync();
                return;
            }

            if (!await _urs.UserOwnsRoleAsync(ctx.Guild.Id, ctx.User.Id, role.Id))
            {
                await Response().Error(strs.userrole_no_permission).SendAsync();
                return;
            }

            var success = await _urs.SetRoleIconAsync(
                ctx.Guild.Id,
                ctx.User.Id,
                role.Id,
                icon
            );

            if (success)
            {
                await Response().Confirm(strs.userrole_icon_success(Format.Bold(role.Name))).SendAsync();
            }
            else
            {
                await Response().Error(strs.userrole_icon_fail).SendAsync();
            }
        }
    }
}