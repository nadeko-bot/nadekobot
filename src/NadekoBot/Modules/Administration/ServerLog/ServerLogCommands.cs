using NadekoBot.Common.TypeReaders.Models;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Administration;

public partial class Administration
{
    [Group]
    [NoPublicBot]
    public partial class LogCommands : NadekoModule<ILogCommandService>
    {
        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [OwnerOnly]
        public async Task LogServer(PermissionAction action)
        {
            await _service.LogServer(ctx.Guild.Id, ctx.Channel.Id, action.Value);
            if (action.Value)
                await Response().Confirm(strs.log_all).SendAsync();
            else
                await Response().Confirm(strs.log_disabled).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [OwnerOnly]
        public async Task LogIgnore()
        {
            var settings = _service.GetGuildLogSettings(ctx.Guild.Id);

            var chs = settings?.LogIgnores.Where(x => x.ItemType == IgnoredItemType.Channel).ToList()
                      ?? new List<IgnoredLogItem>();
            var usrs = settings?.LogIgnores.Where(x => x.ItemType == IgnoredItemType.User).ToList()
                       ?? new List<IgnoredLogItem>();

            var eb = CreateEmbed()
                        .WithOkColor()
                        .AddField(GetText(strs.log_ignored_channels),
                            chs.Count == 0
                                ? "-"
                                : string.Join('\n', chs.Select(x => $"{x.LogItemId} | <#{x.LogItemId}>")))
                        .AddField(GetText(strs.log_ignored_users),
                            usrs.Count == 0
                                ? "-"
                                : string.Join('\n', usrs.Select(x => $"{x.LogItemId} | <@{x.LogItemId}>")));

            await Response().Embed(eb).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [OwnerOnly]
        public async Task LogIgnore([Leftover] ITextChannel target)
        {
            var removed = _service.LogIgnore(ctx.Guild.Id, target.Id, IgnoredItemType.Channel);

            if (!removed)
            {
                await Response()
                      .Confirm(
                          strs.log_ignore_chan(Format.Bold(target.Mention + "(" + target.Id + ")")))
                      .SendAsync();
            }
            else
            {
                await Response()
                      .Confirm(
                          strs.log_not_ignore_chan(Format.Bold(target.Mention + "(" + target.Id + ")")))
                      .SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [OwnerOnly]
        public async Task LogIgnore([Leftover] IUser target)
        {
            var removed = _service.LogIgnore(ctx.Guild.Id, target.Id, IgnoredItemType.User);

            if (!removed)
            {
                await Response()
                      .Confirm(strs.log_ignore_user(Format.Bold(target.Mention + "(" + target.Id + ")")))
                      .SendAsync();
            }
            else
            {
                await Response()
                      .Confirm(strs.log_not_ignore_user(Format.Bold(target.Mention + "(" + target.Id + ")")))
                      .SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [OwnerOnly]
        public async Task LogEvents()
        {
            var logSetting = _service.GetGuildLogSettings(ctx.Guild.Id);
            var str = string.Join("\n",
                Enum.GetNames<LogType>()
                    .Select(x =>
                    {
                        var val = logSetting is null ? null : GetLogProperty(logSetting, Enum.Parse<LogType>(x));
                        if (val is not null)
                            return $"{Format.Bold(x)} <#{val}>";
                        return Format.Bold(x);
                    }));

            await Response().Confirm(Format.Bold(GetText(strs.log_events)) + "\n" + str).SendAsync();
        }

        private static ulong? GetLogProperty(LogSetting l, LogType type)
        {
            switch (type)
            {
                case LogType.Other:
                    return l.LogOtherId;
                case LogType.MessageUpdated:
                    return l.MessageUpdatedId;
                case LogType.MessageDeleted:
                    return l.MessageDeletedId;
                case LogType.UserJoined:
                    return l.UserJoinedId;
                case LogType.UserLeft:
                    return l.UserLeftId;
                case LogType.UserBanned:
                    return l.UserBannedId;
                case LogType.UserUnbanned:
                    return l.UserUnbannedId;
                case LogType.UserUpdated:
                    return l.UserUpdatedId;
                case LogType.ChannelCreated:
                    return l.ChannelCreatedId;
                case LogType.ChannelDestroyed:
                    return l.ChannelDestroyedId;
                case LogType.ChannelUpdated:
                    return l.ChannelUpdatedId;
                case LogType.UserPresence:
                    return l.LogUserPresenceId;
                case LogType.VoicePresence:
                    return l.LogVoicePresenceId;
                case LogType.UserMuted:
                    return l.UserMutedId;
                case LogType.UserWarned:
                    return l.LogWarnsId;
                case LogType.ThreadDeleted:
                    return l.ThreadDeletedId;
                case LogType.ThreadCreated:
                    return l.ThreadCreatedId;
                default:
                    return null;
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [OwnerOnly]
        public async Task Log(LogType type)
        {
            var val = _service.Log(ctx.Guild.Id, ctx.Channel.Id, type);

            if (val)
                await Response().Confirm(strs.log(Format.Bold(type.ToString()))).SendAsync();
            else
                await Response().Confirm(strs.log_stop(Format.Bold(type.ToString()))).SendAsync();
        }
    }
}