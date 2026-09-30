using NadekoBot.Modules.Administration.Services;
using NadekoBot.Modules.Utility.LineUp;
using System.Text;

namespace NadekoBot.Modules.Utility;

public partial class Utility
{
    [Group]
    public partial class LineUpCommands(GuildTimezoneService timezones)
        : NadekoModule<LineUpService>
    {
        private const int LIST_PAGE_SIZE = 10;

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task LineUp([Leftover] string? reason = null)
        {
            var (success, position) =
                await _service.TryJoinLineupAsync(ctx.Guild.Id, ctx.Channel.Id, ctx.User.Id, reason?.TrimTo(200));

            if (success)
            {
                await Response().Confirm(strs.lineup_join_success(Format.Bold(ctx.User.ToString()), position)).SendAsync();
            }
            else
            {
                var currentPosition = await _service.GetPositionAsync(ctx.Guild.Id, ctx.Channel.Id, ctx.User.Id);
                await Response().Error(strs.lineup_already_in(Format.Bold(ctx.User.ToString()), currentPosition ?? 0))
                    .SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task LineUpLeave()
        {
            var success = await _service.TryLeaveLineupAsync(ctx.Guild.Id, ctx.Channel.Id, ctx.User.Id);

            if (success)
                await Response().Confirm(strs.lineup_leave_success).SendAsync();
            else
                await Response().Error(strs.lineup_not_in).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task LineUpList()
        {
            var count = await _service.GetLineupCountAsync(ctx.Guild.Id, ctx.Channel.Id);

            if (count == 0)
            {
                await Response().Confirm(strs.lineup_empty).SendAsync();
                return;
            }

            var tz = timezones.GetTimeZoneOrUtc(ctx.Guild.Id);

            await Response()
                .Paginated()
                .PageItems(page => _service.GetLineupPageAsync(ctx.Guild.Id, ctx.Channel.Id, page, LIST_PAGE_SIZE))
                .TotalElements(count)
                .PageSize(LIST_PAGE_SIZE)
                .Page((pageItems, pageIndex) =>
                {
                    var embed = CreateEmbed()
                        .WithTitle(GetText(strs.lineup_list_title(ctx.Channel.Name)))
                        .WithOkColor();

                    var sb = new StringBuilder();
                    for (var i = 0; i < pageItems.Count; i++)
                    {
                        var user = pageItems[i];
                        var addedTime = TimeZoneInfo.ConvertTime(user.DateAdded, tz);
                        var userName = (ctx.Guild as SocketGuild)?.GetUser(user.UserId)?.ToString() ?? user.UserId.ToString();
                        sb.AppendLine(
                            $"`{pageIndex * LIST_PAGE_SIZE + i + 1}.` {Format.Bold(userName)} ({GetText(strs.lineup_added_at(addedTime))}){(string.IsNullOrWhiteSpace(user.Reason) ? string.Empty : $" - {user.Reason}")}");
                    }

                    embed.WithDescription(sb.ToString());
                    return embed;
                })
                .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        public async Task LineUpNext()
        {
            var nextUser = await _service.GetNextInLineupAsync(ctx.Guild.Id, ctx.Channel.Id);

            if (nextUser is null)
            {
                await Response().Error(strs.lineup_empty).SendAsync();
                return;
            }

            var user = await ctx.Guild.GetUserAsync(nextUser.UserId);
            var userName = user?.Mention ?? $"<@{nextUser.UserId}> ({GetText(strs.user_not_found)})";
            await Response().Confirm(strs.lineup_next_user(userName)).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        public async Task LineUpCreate()
        {
            if (await _service.GetLineupCountAsync(ctx.Guild.Id, ctx.Channel.Id) > 0)
            {
                await Response().Confirm(strs.lineup_already_active).SendAsync();
                return;
            }

            await Response().Confirm(strs.lineup_created).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        public async Task LineUpRemove([Leftover] IGuildUser userToRemove)
        {
            var success = await _service.TryLeaveLineupAsync(ctx.Guild.Id, ctx.Channel.Id, userToRemove.Id);

            if (success)
                await Response().Confirm(strs.lineup_removed(Format.Bold(userToRemove.ToString()))).SendAsync();
            else
                await Response().Error(strs.lineup_remove_fail(Format.Bold(userToRemove.ToString()))).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        public async Task LineUpClear()
        {
            var count = await _service.ClearLineupAsync(ctx.Guild.Id, ctx.Channel.Id);

            if (count == 0)
                await Response().Confirm(strs.lineup_empty).SendAsync();
            else
                await Response().Confirm(strs.lineup_cleared(count)).SendAsync();
        }
    }
}
