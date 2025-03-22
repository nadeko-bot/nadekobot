using NadekoBot.Common.TypeReaders.Models;
using NadekoBot.Modules.Utility.Scheduled;

namespace NadekoBot.Modules.Utility;

public partial class Utility
{
    [Group]
    public class ScheduledCommands(ScheduleCommandService scs) : NadekoModule
    {
        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task ScheduleList()
        {
            var scheduledCommands = await scs.GetUserScheduledCommandsAsync(ctx.Guild.Id, ctx.User.Id);

            if (scheduledCommands.Count == 0)
            {
                await Response().Error(strs.schedule_list_none).SendAsync();
                return;
            }

            await Response()
                .Paginated()
                .Items(scheduledCommands)
                .PageSize(5)
                .Page((pageCommands, _) =>
                {
                    var eb = CreateEmbed()
                        .WithTitle(GetText(strs.schedule_list_title))
                        .WithAuthor(ctx.User)
                        .WithOkColor();

                    foreach (var cmd in pageCommands)
                    {
                        eb.AddField(
                            $"`{GetText(strs.schedule_id)}:` {(kwum)cmd.Id}",
                            $"""
                             `{GetText(strs.schedule_command)}:` {cmd.Text}
                             `{GetText(strs.schedule_when)}:` {TimestampTag.FromDateTime(cmd.When, TimestampTagStyles.Relative)}
                             """);
                    }

                    return eb;
                })
                .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task ScheduleDelete([Leftover] kwum id)
        {
            var success = await scs.DeleteScheduledCommandAsync(id, ctx.Guild.Id, ctx.User.Id);

            if (success)
            {
                await Response().Confirm(strs.schedule_deleted(id)).SendAsync();
            }
            else
            {
                await Response().Error(strs.schedule_delete_error).SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task ScheduleAdd(ParsedTimespan timeString, [Leftover] string commandText)
        {
            if (timeString.Time < TimeSpan.FromMinutes(1))
                return;

            var success = await scs.AddScheduledCommandAsync(
                ctx.Guild.Id,
                ctx.Channel.Id,
                ctx.Message.Id,
                ctx.User.Id,
                commandText,
                timeString.Time);

            if (success)
            {
                await Response().Confirm(strs.schedule_add_success).SendAsync();
            }
            else
            {
                await Response().Error(strs.schedule_add_error).SendAsync();
            }
        }
    }
}