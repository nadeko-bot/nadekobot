#nullable disable
using NadekoBot.Modules.Administration.Services;

namespace NadekoBot.Modules.Administration;

public partial class Administration
{
    [Group]
    public partial class VcRoleCommands : NadekoModule<VcRoleService>
    {
        [Cmd]
        [UserPerm(GuildPerm.ManageRoles)]
        [BotPerm(GuildPerm.ManageRoles)]
        [RequireContext(ContextType.Guild)]
        public async Task VcRoleRm(ulong vcId)
        {
            if (_service.RemoveVcRole(ctx.Guild.Id, vcId))
                await Response().Confirm(strs.vcrole_removed(Format.Bold(vcId.ToString()))).SendAsync();
            else
                await Response().Error(strs.vcrole_not_found).SendAsync();
        }

        [Cmd]
        [UserPerm(GuildPerm.ManageRoles)]
        [BotPerm(GuildPerm.ManageRoles)]
        [RequireContext(ContextType.Guild)]
        public async Task VcRole([Leftover] IRole role = null)
        {
            var user = (IGuildUser)ctx.User;

            var vc = user.VoiceChannel;

            if (vc is null || vc.GuildId != user.GuildId)
            {
                await Response().Error(strs.must_be_in_voice).SendAsync();
                return;
            }

            if (role is null)
            {
                if (_service.RemoveVcRole(ctx.Guild.Id, vc.Id))
                    await Response().Confirm(strs.vcrole_removed(Format.Bold(vc.Name))).SendAsync();
            }
            else
            {
                _service.AddVcRole(ctx.Guild.Id, role, vc.Id);
                await Response().Confirm(strs.vcrole_added(Format.Bold(vc.Name), Format.Bold(role.Name))).SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task VcRoleList()
        {
            var guild = (SocketGuild)ctx.Guild;
            string text;
            if (_service.VcRoles.TryGetValue(ctx.Guild.Id, out var roles))
            {
                if (!roles.Any())
                    text = GetText(strs.no_vcroles);
                else
                {
                    text = string.Join("\n",
                        roles.Select(x
                            => $"{Format.Bold(guild.GetVoiceChannel(x.Key)?.Name ?? x.Key.ToString())} => {x.Value}"));
                }
            }
            else
                text = GetText(strs.no_vcroles);

            await Response().Embed(CreateEmbed()
                                            .WithOkColor()
                                            .WithTitle(GetText(strs.vc_role_list))
                                            .WithDescription(text)).SendAsync();
        }
    }
}