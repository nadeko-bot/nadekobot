#nullable disable
using Google.Protobuf.WellKnownTypes;
using NadekoBot.Common.TypeReaders.Models;
using SixLabors.ImageSharp.PixelFormats;
using Color = SixLabors.ImageSharp.Color;

namespace NadekoBot.Modules.Administration;

public partial class Administration
{
    public partial class RoleCommands : NadekoModule
    {
        public enum Exclude
        {
            Excl
        }

        private readonly TempRoleService _tempRoleService;
        private readonly IServiceProvider _services;
        private StickyRolesService _stickyRoleSvc;

        public RoleCommands(
            IServiceProvider services,
            StickyRolesService stickyRoleSvc,
            TempRoleService tempRoleService)
        {
            _services = services;
            _stickyRoleSvc = stickyRoleSvc;
            _tempRoleService = tempRoleService;
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        [BotPerm(GuildPerm.ManageRoles)]
        public async Task SetRole(IGuildUser targetUser, [Leftover] IRole roleToAdd)
        {
            var runnerUser = (IGuildUser)ctx.User;
            var runnerMaxRolePosition = runnerUser.GetRoles().Max(x => x.Position);
            if (ctx.User.Id != ctx.Guild.OwnerId && runnerMaxRolePosition <= roleToAdd.Position)
                return;
            try
            {
                await targetUser.AddRoleAsync(roleToAdd,
                    new RequestOptions()
                    {
                        AuditLogReason = $"Added by [{ctx.User.Username}]"
                    });

                await Response()
                      .Confirm(strs.setrole(Format.Bold(roleToAdd.Name),
                          Format.Bold(targetUser.ToString())))
                      .SendAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error in setrole command");
                await Response().Error(strs.setrole_err).SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        [BotPerm(GuildPerm.ManageRoles)]
        public async Task RemoveRole(IGuildUser targetUser, [Leftover] IRole roleToRemove)
        {
            var runnerUser = (IGuildUser)ctx.User;
            if (ctx.User.Id != runnerUser.Guild.OwnerId
                && runnerUser.GetRoles().Max(x => x.Position) <= roleToRemove.Position)
                return;
            try
            {
                await targetUser.RemoveRoleAsync(roleToRemove);
                await Response()
                      .Confirm(strs.remrole(Format.Bold(roleToRemove.Name),
                          Format.Bold(targetUser.ToString())))
                      .SendAsync();
            }
            catch
            {
                await Response().Error(strs.remrole_err).SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        [BotPerm(GuildPerm.ManageRoles)]
        public async Task RenameRole(IRole roleToEdit, [Leftover] string newname)
        {
            var guser = (IGuildUser)ctx.User;
            if (ctx.User.Id != guser.Guild.OwnerId && guser.GetRoles().Max(x => x.Position) <= roleToEdit.Position)
                return;
            try
            {
                if (roleToEdit.Position > (await ctx.Guild.GetCurrentUserAsync()).GetRoles().Max(r => r.Position))
                {
                    await Response().Error(strs.renrole_perms).SendAsync();
                    return;
                }

                await roleToEdit.ModifyAsync(g => g.Name = newname);
                await Response().Confirm(strs.renrole).SendAsync();
            }
            catch (Exception)
            {
                await Response().Error(strs.renrole_err).SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        [BotPerm(GuildPerm.ManageRoles)]
        public async Task RemoveAllRoles([Leftover] IGuildUser user)
        {
            var guser = (IGuildUser)ctx.User;

            var userRoles = user.GetRoles().Where(x => !x.IsManaged && x != x.Guild.EveryoneRole).ToList();

            if (user.Id == ctx.Guild.OwnerId
                || (ctx.User.Id != ctx.Guild.OwnerId
                    && guser.GetRoles().Max(x => x.Position) <= userRoles.Max(x => x.Position)))
                return;
            try
            {
                await user.RemoveRolesAsync(userRoles);
                await Response().Confirm(strs.rar(Format.Bold(user.ToString()))).SendAsync();
            }
            catch (Exception)
            {
                await Response().Error(strs.rar_err).SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        [BotPerm(GuildPerm.ManageRoles)]
        public async Task CreateRole([Leftover] string roleName = null)
        {
            if (string.IsNullOrWhiteSpace(roleName))
                return;

            var r = await ctx.Guild.CreateRoleAsync(roleName, isMentionable: false);
            await Response().Confirm(strs.cr(Format.Bold(r.Name))).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        [BotPerm(GuildPerm.ManageRoles)]
        public async Task DeleteRole([Leftover] IRole role)
        {
            var guser = (IGuildUser)ctx.User;
            if (ctx.User.Id != guser.Guild.OwnerId && guser.GetRoles().Max(x => x.Position) <= role.Position)
                return;

            await role.DeleteAsync();
            await Response().Confirm(strs.dr(Format.Bold(role.Name))).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        [BotPerm(GuildPerm.ManageRoles)]
        public async Task RoleHoist([Leftover] IRole role)
        {
            var newHoisted = !role.IsHoisted;
            await role.ModifyAsync(r => r.Hoist = newHoisted);
            if (newHoisted)
                await Response().Confirm(strs.rolehoist_enabled(Format.Bold(role.Name))).SendAsync();
            else
                await Response().Confirm(strs.rolehoist_disabled(Format.Bold(role.Name))).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [Priority(1)]
        public async Task RoleColor([Leftover] IRole role)
            => await Response().Confirm("Role Color", role.Color.RawValue.ToString("x6")).SendAsync();

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        [BotPerm(GuildPerm.ManageRoles)]
        [Priority(0)]
        public async Task RoleColor(Rgba32 color, [Leftover] IRole role)
        {
            try
            {
                await role.ModifyAsync(r => r.Color = new Discord.Color(color.R, color.G, color.B));
                await Response().Confirm(strs.rc(Format.Bold(role.Name))).SendAsync();
            }
            catch (Exception)
            {
                await Response().Error(strs.rc_perms).SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [BotPerm(GuildPerm.ManageRoles)]
        public async Task StickyRoles()
        {
            var newState = await _stickyRoleSvc.ToggleStickyRoles(ctx.Guild.Id);

            if (newState)
            {
                await Response().Confirm(strs.sticky_roles_enabled).SendAsync();
            }
            else
            {
                await Response().Confirm(strs.sticky_roles_disabled).SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [BotPerm(GuildPerm.ManageRoles)]
        public async Task TempRole(ParsedTimespan timespan, IGuildUser user, [Leftover] IRole role)
        {
            if (!await CheckRoleHierarchy(role))
            {
                await Response()
                      .Error(strs.hierarchy)
                      .SendAsync();
                return;
            }

            await user.AddRoleAsync(role);
            await _tempRoleService.AddTempRoleAsync(ctx.Guild.Id, role.Id, user.Id, timespan.Time);
            

            await Response()
                  .Confirm(strs.temp_role_added(user.Mention,
                      Format.Bold(role.Name),
                      TimestampTag.FromDateTime(DateTime.UtcNow.Add(timespan.Time), TimestampTagStyles.Relative)))
                  .SendAsync();
        }
    }
}