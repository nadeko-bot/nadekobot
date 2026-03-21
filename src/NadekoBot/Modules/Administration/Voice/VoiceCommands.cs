#nullable disable
using NadekoBot.Modules.Administration.Services;

namespace NadekoBot.Modules.Administration;

public partial class Administration
{
    [Group]
    public partial class VoiceCommands(
        AdministrationService service,
        GameVoiceChannelService gvcService,
        VcRoleService vcRoleService) : NadekoModule<AdministrationService>
    {
        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.DeafenMembers)]
        [BotPerm(GuildPerm.DeafenMembers)]
        public async Task Deafen(params IGuildUser[] users)
        {
            await service.DeafenUsers(true, users);
            await Response().Confirm(strs.deafen).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.DeafenMembers)]
        [BotPerm(GuildPerm.DeafenMembers)]
        public async Task UnDeafen(params IGuildUser[] users)
        {
            await service.DeafenUsers(false, users);
            await Response().Confirm(strs.undeafen).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageChannels)]
        [BotPerm(GuildPerm.ManageChannels)]
        public async Task DelVoiChanl([Leftover] IVoiceChannel voiceChannel)
        {
            await voiceChannel.DeleteAsync();
            await Response().Confirm(strs.delvoich(Format.Bold(voiceChannel.Name))).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageChannels)]
        [BotPerm(GuildPerm.ManageChannels)]
        public async Task CreatVoiChanl([Leftover] string channelName)
        {
            var ch = await ctx.Guild.CreateVoiceChannelAsync(channelName);
            await Response().Confirm(strs.createvoich(Format.Bold(ch.Name))).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [BotPerm(GuildPerm.MoveMembers)]
        public async Task GameVoiceChannel()
        {
            var vch = ((IGuildUser)ctx.User).VoiceChannel;

            if (vch is null)
            {
                await Response().Error(strs.not_in_voice).SendAsync();
                return;
            }

            var id = gvcService.ToggleGameVoiceChannel(ctx.Guild.Id, vch.Id);

            if (id is null)
            {
                await Response().Confirm(strs.gvc_disabled).SendAsync();
            }
            else
            {
                await Response().Confirm(strs.gvc_enabled(Format.Bold(vch.Name))).SendAsync();
            }
        }

        [Cmd]
        [UserPerm(GuildPerm.ManageRoles)]
        [BotPerm(GuildPerm.ManageRoles)]
        [RequireContext(ContextType.Guild)]
        public async Task VcRoleRm(ulong vcId)
        {
            if (vcRoleService.RemoveVcRole(ctx.Guild.Id, vcId))
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

            if (!await CheckRoleHierarchy(role))
            {
                await Response().Error(strs.hierarchy).SendAsync();
                return;
            }
            
            if (vc is null || vc.GuildId != user.GuildId)
            {
                await Response().Error(strs.must_be_in_voice).SendAsync();
                return;
            }

            if (role is null)
            {
                if (vcRoleService.RemoveVcRole(ctx.Guild.Id, vc.Id))
                    await Response().Confirm(strs.vcrole_removed(Format.Bold(vc.Name))).SendAsync();
            }
            else
            {
                vcRoleService.AddVcRole(ctx.Guild.Id, role, vc.Id);
                await Response().Confirm(strs.vcrole_added(Format.Bold(vc.Name), Format.Bold(role.Name))).SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task VcRoleList()
        {
            var guild = (SocketGuild)ctx.Guild;
            string text;
            if (vcRoleService.VcRoles.TryGetValue(ctx.Guild.Id, out var roles))
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
