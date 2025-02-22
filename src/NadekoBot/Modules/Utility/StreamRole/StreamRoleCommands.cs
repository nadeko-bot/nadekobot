#nullable disable
using NadekoBot.Modules.Utility.Common;
using NadekoBot.Modules.Utility.Services;

namespace NadekoBot.Modules.Utility;

public partial class Utility
{
    public partial class StreamRoleCommands : NadekoModule<StreamRoleService>
    {
        [Cmd]
        [BotPerm(GuildPerm.ManageRoles)]
        [UserPerm(GuildPerm.ManageRoles)]
        [RequireContext(ContextType.Guild)]
        public async Task StreamRole(IRole fromRole, IRole addRole)
        {
            await _service.SetStreamRole(fromRole, addRole);

            await Response().Confirm(strs.stream_role_enabled(Format.Bold(fromRole.ToString()),
                Format.Bold(addRole.ToString()))).SendAsync();
        }

        [Cmd]
        [BotPerm(GuildPerm.ManageRoles)]
        [UserPerm(GuildPerm.ManageRoles)]
        [RequireContext(ContextType.Guild)]
        public async Task StreamRole()
        {
            await _service.StopStreamRole(ctx.Guild);
            await Response().Confirm(strs.stream_role_disabled).SendAsync();
        }

        [Cmd]
        [BotPerm(GuildPerm.ManageRoles)]
        [UserPerm(GuildPerm.ManageRoles)]
        [RequireContext(ContextType.Guild)]
        public async Task StreamRoleKeyword([Leftover] string keyword = null)
        {
            var kw = await _service.SetKeyword(ctx.Guild, keyword);

            if (string.IsNullOrWhiteSpace(keyword))
                await Response().Confirm(strs.stream_role_kw_reset).SendAsync();
            else
                await Response().Confirm(strs.stream_role_kw_set(Format.Bold(kw))).SendAsync();
        }

        [Cmd]
        [BotPerm(GuildPerm.ManageRoles)]
        [UserPerm(GuildPerm.ManageRoles)]
        [RequireContext(ContextType.Guild)]
        public async Task StreamRoleBlacklist(AddRemove action, [Leftover] IGuildUser user)
        {
            var success = await _service.ApplyListAction(StreamRoleListType.Blacklist,
                ctx.Guild,
                action,
                user.Id,
                user.ToString());

            if (action == AddRemove.Add)
            {
                if (success)
                    await Response().Confirm(strs.stream_role_bl_add(Format.Bold(user.ToString()))).SendAsync();
                else
                    await Response().Confirm(strs.stream_role_bl_add_fail(Format.Bold(user.ToString()))).SendAsync();
            }
            else if (success)
                await Response().Confirm(strs.stream_role_bl_rem(Format.Bold(user.ToString()))).SendAsync();
            else
                await Response().Error(strs.stream_role_bl_rem_fail(Format.Bold(user.ToString()))).SendAsync();
        }

        [Cmd]
        [BotPerm(GuildPerm.ManageRoles)]
        [UserPerm(GuildPerm.ManageRoles)]
        [RequireContext(ContextType.Guild)]
        public async Task StreamRoleWhitelist(AddRemove action, [Leftover] IGuildUser user)
        {
            var success = await _service.ApplyListAction(StreamRoleListType.Whitelist,
                ctx.Guild,
                action,
                user.Id,
                user.ToString());

            if (action == AddRemove.Add)
            {
                if (success)
                    await Response().Confirm(strs.stream_role_wl_add(Format.Bold(user.ToString()))).SendAsync();
                else
                    await Response().Confirm(strs.stream_role_wl_add_fail(Format.Bold(user.ToString()))).SendAsync();
            }
            else if (success)
                await Response().Confirm(strs.stream_role_wl_rem(Format.Bold(user.ToString()))).SendAsync();
            else
                await Response().Error(strs.stream_role_wl_rem_fail(Format.Bold(user.ToString()))).SendAsync();
        }
    }
}