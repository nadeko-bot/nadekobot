#nullable disable
using NadekoBot.Modules.Utility.Services;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Utility;

public partial class Utility
{
    [Group]
    public partial class RemindCommands : NadekoModule<RemindService>
    {
        public enum MeOrHere
        {
            Me,
            Here
        }

        public enum Server
        {
            Server = int.MinValue,
            Srvr = int.MinValue,
            Serv = int.MinValue,
            S = int.MinValue
        }

        private readonly DbService _db;
        private readonly ITimezoneService _tz;

        public RemindCommands(DbService db, ITimezoneService tz)
        {
            _db = db;
            _tz = tz;
        }

        [Cmd]
        [Priority(1)]
        public async Task Remind(MeOrHere meorhere, [Leftover] string remindString)
        {
            if (!_service.TryParseRemindMessage(remindString, out var remindData))
            {
                await Response().Error(strs.remind_invalid).SendAsync();
                return;
            }

            ulong target;
            target = meorhere == MeOrHere.Me ? ctx.User.Id : ctx.Channel.Id;

            var success = await RemindInternal(target,
                meorhere == MeOrHere.Me || ctx.Guild is null,
                remindData.Time,
                remindData.What,
                ReminderType.User);

            if (!success)
                await Response().Error(strs.remind_too_long).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        [Priority(0)]
        public async Task Remind(ITextChannel channel, [Leftover] string remindString)
        {
            var perms = ((IGuildUser)ctx.User).GetPermissions(channel);
            if (!perms.SendMessages || !perms.ViewChannel)
            {
                await Response().Error(strs.cant_read_or_send).SendAsync();
                return;
            }

            if (!_service.TryParseRemindMessage(remindString, out var remindData))
            {
                await Response().Error(strs.remind_invalid).SendAsync();
                return;
            }


            var success = await RemindInternal(channel.Id, false, remindData.Time, remindData.What, ReminderType.User);
            if (!success)
                await Response().Error(strs.remind_too_long).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [Priority(0)]
        public Task RemindList(Server _, int page = 1)
            => RemindListInternal(page, ctx.Guild.Id);

        [Cmd]
        [Priority(1)]
        public Task RemindList(int page = 1)
            => RemindListInternal(page, null);

        private async Task RemindListInternal(int page, ulong? guildId)
        {
            if (--page < 0)
                return;

            var embed = CreateEmbed()
                        .WithOkColor()
                        .WithTitle(GetText(guildId is not null
                            ? strs.reminder_server_list
                            : strs.reminder_list));

            List<Reminder> rems;
            if (guildId is { } gid)
                rems = await _service.GetServerReminders(page, gid);
            else
                rems = await _service.GetUserReminders(page, ctx.User.Id);


            if (rems.Count > 0)
            {
                var i = 0;
                foreach (var rem in rems)
                {
                    var when = rem.When;
                    embed.AddField(
                        $"#{++i + (page * 10)}",
                        $"""
                         `When:` {TimestampTag.FromDateTime(when, TimestampTagStyles.ShortDateTime)}
                         `Target:` {(rem.IsPrivate ? "DM" : "Channel")} [`{rem.ChannelId}`]
                         `Message:` {rem.Message?.TrimTo(50)}
                         """);
                }
            }
            else
            {
                embed.WithDescription(GetText(strs.reminders_none));
            }

            embed.AddPaginatedFooter(page + 1, null);
            await Response().Embed(embed).SendAsync();
        }


        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [Priority(0)]
        public Task RemindDelete(Server _, int index)
            => RemindDelete(index, true);

        [Cmd]
        [Priority(1)]
        public Task RemindDelete(int index)
            => RemindDelete(index, false);

        private async Task RemindDelete(int index, bool isServer)
        {
            if (--index < 0)
                return;

            Reminder rem = null;
            await using (var uow = _db.GetDbContext())
            {
                var rems = isServer
                    ? uow.Set<Reminder>().RemindersForServer(ctx.Guild.Id, index / 10).ToList()
                    : uow.Set<Reminder>().RemindersFor(ctx.User.Id, index / 10).ToList();

                var pageIndex = index % 10;
                if (rems.Count > pageIndex)
                {
                    rem = rems[pageIndex];
                    uow.Set<Reminder>().Remove(rem);
                    uow.SaveChanges();
                }
            }

            if (rem is null)
                await Response().Error(strs.reminder_not_exist).SendAsync();
            else
                await Response().Confirm(strs.reminder_deleted(index + 1)).SendAsync();
        }

        private async Task<bool> RemindInternal(
            ulong targetId,
            bool isPrivate,
            TimeSpan ts,
            string message,
            ReminderType reminderType)
        {
            var time = DateTime.UtcNow + ts;

            if (ts > TimeSpan.FromDays(366))
                return false;

            if (ctx.Guild is not null)
            {
                var perms = ((IGuildUser)ctx.User).GetPermissions((IGuildChannel)ctx.Channel);
                if (!perms.MentionEveryone)
                    message = message.SanitizeAllMentions();
            }

            await _service.AddReminderAsync(ctx.User.Id,
                targetId,
                ctx.Guild?.Id,
                isPrivate,
                time,
                message,
                ReminderType.User);

            await Response()
                  .Confirm($"\u23f0 {GetText(strs.remind2(
                      Format.Bold(!isPrivate ? $"<#{targetId}>" : ctx.User.Username),
                      Format.Bold(message),
                      TimestampTag.FromDateTime(DateTime.UtcNow.Add(ts), TimestampTagStyles.Relative),
                      TimestampTag.FormatFromDateTime(time, TimestampTagStyles.ShortDateTime)))}")
                  .SendAsync();

            return true;
        }
    }
}