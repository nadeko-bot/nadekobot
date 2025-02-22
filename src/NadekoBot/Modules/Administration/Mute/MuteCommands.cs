#nullable disable
using NadekoBot.Common.TypeReaders.Models;
using NadekoBot.Modules.Administration.Services;

namespace NadekoBot.Modules.Administration;

public partial class Administration
{
    [Group]
    public partial class MuteCommands : NadekoModule<MuteService>
    {
        private async Task<bool> VerifyMutePermissions(IGuildUser runnerUser, IGuildUser targetUser)
        {
            var runnerUserRoles = runnerUser.GetRoles();
            var targetUserRoles = targetUser.GetRoles();
            if (runnerUser.Id != ctx.Guild.OwnerId
                && runnerUserRoles.Max(x => x.Position) <= targetUserRoles.Max(x => x.Position))
            {
                await Response().Error(strs.mute_perms).SendAsync();
                return false;
            }

            return true;
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        public async Task MuteRole([Leftover] IRole role = null)
        {
            if (role is null)
            {
                var muteRole = await _service.GetMuteRole(ctx.Guild);
                await Response().Confirm(strs.mute_role(Format.Code(muteRole.Name))).SendAsync();
                return;
            }

            if (ctx.User.Id != ctx.Guild.OwnerId
                && role.Position >= ((SocketGuildUser)ctx.User).Roles.Max(x => x.Position))
            {
                await Response().Error(strs.insuf_perms_u).SendAsync();
                return;
            }

            await _service.SetMuteRoleAsync(ctx.Guild.Id, role.Name);

            await Response().Confirm(strs.mute_role_set).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles | GuildPerm.MuteMembers)]
        [Priority(0)]
        public async Task Mute(IGuildUser target, [Leftover] string reason = "")
        {
            try
            {
                if (!await VerifyMutePermissions((IGuildUser)ctx.User, target))
                    return;

                await _service.MuteUser(target, ctx.User, reason: reason);
                await Response().Confirm(strs.user_muted(Format.Bold(target.ToString()))).SendAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Exception in the mute command");
                await Response().Error(strs.mute_error).SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles | GuildPerm.MuteMembers)]
        [Priority(1)]
        public async Task Mute(ParsedTimespan timespan, IGuildUser user, [Leftover] string reason = "")
        {
            if (timespan.Time < TimeSpan.FromMinutes(1) || timespan.Time > TimeSpan.FromDays(49))
                return;
            try
            {
                if (!await VerifyMutePermissions((IGuildUser)ctx.User, user))
                    return;

                await _service.TimedMute(user, ctx.User, timespan.Time, reason: reason);
                await Response().Confirm(strs.user_muted_time(Format.Bold(user.ToString()),
                    (int)timespan.Time.TotalMinutes)).SendAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error in mute command");
                await Response().Error(strs.mute_error).SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles | GuildPerm.MuteMembers)]
        public async Task Unmute(IGuildUser user, [Leftover] string reason = "")
        {
            try
            {
                await _service.UnmuteUser(user.GuildId, user.Id, ctx.User, reason: reason);
                await Response().Confirm(strs.user_unmuted(Format.Bold(user.ToString()))).SendAsync();
            }
            catch
            {
                await Response().Error(strs.mute_error).SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        [Priority(0)]
        public async Task ChatMute(IGuildUser user, [Leftover] string reason = "")
        {
            try
            {
                if (!await VerifyMutePermissions((IGuildUser)ctx.User, user))
                    return;

                await _service.MuteUser(user, ctx.User, MuteType.Chat, reason);
                await Response().Confirm(strs.user_chat_mute(Format.Bold(user.ToString()))).SendAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Exception in the chatmute command");
                await Response().Error(strs.mute_error).SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        [Priority(1)]
        public async Task ChatMute(ParsedTimespan timespan, IGuildUser user, [Leftover] string reason = "")
        {
            if (timespan.Time < TimeSpan.FromMinutes(1) || timespan.Time > TimeSpan.FromDays(49))
                return;
            try
            {
                if (!await VerifyMutePermissions((IGuildUser)ctx.User, user))
                    return;

                await _service.TimedMute(user, ctx.User, timespan.Time, MuteType.Chat, reason);
                await Response().Confirm(strs.user_chat_mute_time(Format.Bold(user.ToString()),
                    (int)timespan.Time.TotalMinutes)).SendAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error in chatmute command");
                await Response().Error(strs.mute_error).SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        public async Task ChatUnmute(IGuildUser user, [Leftover] string reason = "")
        {
            try
            {
                await _service.UnmuteUser(user.Guild.Id, user.Id, ctx.User, MuteType.Chat, reason);
                await Response().Confirm(strs.user_chat_unmute(Format.Bold(user.ToString()))).SendAsync();
            }
            catch
            {
                await Response().Error(strs.mute_error).SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.MuteMembers)]
        [Priority(0)]
        public async Task VoiceMute(IGuildUser user, [Leftover] string reason = "")
        {
            try
            {
                if (!await VerifyMutePermissions((IGuildUser)ctx.User, user))
                    return;

                await _service.MuteUser(user, ctx.User, MuteType.Voice, reason);
                await Response().Confirm(strs.user_voice_mute(Format.Bold(user.ToString()))).SendAsync();
            }
            catch
            {
                await Response().Error(strs.mute_error).SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.MuteMembers)]
        [Priority(1)]
        public async Task VoiceMute(ParsedTimespan timespan, IGuildUser user, [Leftover] string reason = "")
        {
            if (timespan.Time < TimeSpan.FromMinutes(1) || timespan.Time > TimeSpan.FromDays(49))
                return;
            try
            {
                if (!await VerifyMutePermissions((IGuildUser)ctx.User, user))
                    return;

                await _service.TimedMute(user, ctx.User, timespan.Time, MuteType.Voice, reason);
                await Response().Confirm(strs.user_voice_mute_time(Format.Bold(user.ToString()),
                    (int)timespan.Time.TotalMinutes)).SendAsync();
            }
            catch
            {
                await Response().Error(strs.mute_error).SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.MuteMembers)]
        public async Task VoiceUnmute(IGuildUser user, [Leftover] string reason = "")
        {
            try
            {
                await _service.UnmuteUser(user.GuildId, user.Id, ctx.User, MuteType.Voice, reason);
                await Response().Confirm(strs.user_voice_unmute(Format.Bold(user.ToString()))).SendAsync();
            }
            catch
            {
                await Response().Error(strs.mute_error).SendAsync();
            }
        }
    }
}