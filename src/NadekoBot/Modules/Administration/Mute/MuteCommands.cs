#nullable disable
using NadekoBot.Common.TypeReaders.Models;
using NadekoBot.Modules.Administration.Services;

namespace NadekoBot.Modules.Administration;

public partial class Administration
{
    [Group]
    public partial class MuteCommands : NadekoModule<MuteService>
    {
        private static readonly TimeSpan _minMuteTime = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan _maxMuteTime = TimeSpan.FromDays(49);

        private async Task MuteInternalAsync(IGuildUser target, MuteType type, string reason, LocStr success)
        {
            if (!await CheckRoleHierarchy(target))
                return;

            try
            {
                await _service.MuteUser(target, ctx.User, type, reason);
                await Response().Confirm(success).SendAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error in mute command");
                await Response().Error(strs.mute_error).SendAsync();
            }
        }

        private async Task TimedMuteInternalAsync(
            ParsedTimespan timespan,
            IGuildUser target,
            MuteType type,
            string reason,
            Func<int, LocStr> success)
        {
            if (timespan.Time < _minMuteTime || timespan.Time > _maxMuteTime)
            {
                await Response().Error(strs.mute_time_range).SendAsync();
                return;
            }

            if (!await CheckRoleHierarchy(target))
                return;

            try
            {
                await _service.TimedMute(target, ctx.User, timespan.Time, type, reason);
                await Response().Confirm(success((int)timespan.Time.TotalMinutes)).SendAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error in timed mute command");
                await Response().Error(strs.mute_error).SendAsync();
            }
        }

        private async Task UnmuteInternalAsync(IGuildUser user, MuteType type, string reason, LocStr success)
        {
            if (!await CheckRoleHierarchy(user))
                return;

            try
            {
                await _service.UnmuteUser(user.GuildId, user.Id, ctx.User, type, reason);
                await Response().Confirm(success).SendAsync();
            }
            catch
            {
                await Response().Error(strs.mute_error).SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        public async Task MuteRole([Leftover] IRole role = null)
        {
            if (role is null)
            {
                var muteRole = await _service.GetMuteRole(ctx.Guild);
                if (muteRole is null)
                {
                    await Response().Error(strs.mute_error).SendAsync();
                    return;
                }

                await Response().Confirm(strs.mute_role(Format.Code(muteRole.Name))).SendAsync();
                return;
            }

            if (!await CheckRoleHierarchy(role))
                return;

            if (role.IsManaged || role.Id == ctx.Guild.EveryoneRole.Id)
            {
                await Response().Error(strs.mute_role_invalid).SendAsync();
                return;
            }

            await _service.SetMuteRoleAsync(ctx.Guild.Id, role.Id);
            await Response().Confirm(strs.mute_role_set).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles | GuildPerm.MuteMembers)]
        [BotPerm(GuildPerm.ManageRoles | GuildPerm.MuteMembers)]
        [Priority(0)]
        public Task Mute(IGuildUser target, [Leftover] string reason = "")
            => MuteInternalAsync(target,
                MuteType.All,
                reason,
                strs.user_muted(Format.Bold(target.ToString())));

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles | GuildPerm.MuteMembers)]
        [BotPerm(GuildPerm.ManageRoles | GuildPerm.MuteMembers)]
        [Priority(1)]
        public Task Mute(ParsedTimespan timespan, IGuildUser user, [Leftover] string reason = "")
            => TimedMuteInternalAsync(timespan,
                user,
                MuteType.All,
                reason,
                minutes => strs.user_muted_time(Format.Bold(user.ToString()), minutes));

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles | GuildPerm.MuteMembers)]
        [BotPerm(GuildPerm.ManageRoles | GuildPerm.MuteMembers)]
        public Task Unmute(IGuildUser user, [Leftover] string reason = "")
            => UnmuteInternalAsync(user,
                MuteType.All,
                reason,
                strs.user_unmuted(Format.Bold(user.ToString())));

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        [BotPerm(GuildPerm.ManageRoles)]
        [Priority(0)]
        public Task ChatMute(IGuildUser user, [Leftover] string reason = "")
            => MuteInternalAsync(user,
                MuteType.Chat,
                reason,
                strs.user_chat_mute(Format.Bold(user.ToString())));

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        [BotPerm(GuildPerm.ManageRoles)]
        [Priority(1)]
        public Task ChatMute(ParsedTimespan timespan, IGuildUser user, [Leftover] string reason = "")
            => TimedMuteInternalAsync(timespan,
                user,
                MuteType.Chat,
                reason,
                minutes => strs.user_chat_mute_time(Format.Bold(user.ToString()), minutes));

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        [BotPerm(GuildPerm.ManageRoles)]
        public Task ChatUnmute(IGuildUser user, [Leftover] string reason = "")
            => UnmuteInternalAsync(user,
                MuteType.Chat,
                reason,
                strs.user_chat_unmute(Format.Bold(user.ToString())));

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.MuteMembers)]
        [BotPerm(GuildPerm.MuteMembers)]
        [Priority(0)]
        public Task VoiceMute(IGuildUser user, [Leftover] string reason = "")
            => MuteInternalAsync(user,
                MuteType.Voice,
                reason,
                strs.user_voice_mute(Format.Bold(user.ToString())));

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.MuteMembers)]
        [BotPerm(GuildPerm.MuteMembers)]
        [Priority(1)]
        public Task VoiceMute(ParsedTimespan timespan, IGuildUser user, [Leftover] string reason = "")
            => TimedMuteInternalAsync(timespan,
                user,
                MuteType.Voice,
                reason,
                minutes => strs.user_voice_mute_time(Format.Bold(user.ToString()), minutes));

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.MuteMembers)]
        [BotPerm(GuildPerm.MuteMembers)]
        public Task VoiceUnmute(IGuildUser user, [Leftover] string reason = "")
            => UnmuteInternalAsync(user,
                MuteType.Voice,
                reason,
                strs.user_voice_unmute(Format.Bold(user.ToString())));
    }
}
