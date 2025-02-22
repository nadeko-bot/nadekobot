#nullable disable
using NadekoBot.Modules.Searches.Services;

namespace NadekoBot.Modules.Searches;

public partial class Searches
{
    [Group]
    public partial class StreamNotificationCommands : NadekoModule<StreamNotificationService>
    {
        private readonly DbService _db;

        public StreamNotificationCommands(DbService db)
            => _db = db;

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        public async Task StreamAdd(string link)
        {
            var data = await _service.FollowStream(ctx.Guild.Id, ctx.Channel.Id, link);
            if (data is null)
            {
                await Response().Error(strs.stream_not_added).SendAsync();
                return;
            }

            var embed = _service.GetEmbed(ctx.Guild.Id, data);
            await Response()
                  .Embed(embed)
                  .Text(strs.stream_tracked)
                  .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        [Priority(1)]
        public async Task StreamRemove(int index)
        {
            if (--index < 0)
                return;

            var fs = await _service.UnfollowStreamAsync(ctx.Guild.Id, index);
            if (fs is null)
            {
                await Response().Error(strs.stream_no).SendAsync();
                return;
            }

            await Response().Confirm(strs.stream_removed(Format.Bold(fs.Username), fs.Type)).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task StreamsClear()
        {
            await _service.ClearAllStreams(ctx.Guild.Id);
            await Response().Confirm(strs.streams_cleared).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task StreamList(int page = 1)
        {
            if (page-- < 1)
                return;

            var allStreams = await _service.GetAllStreamsAsync((SocketGuild)ctx.Guild);

            await Response()
                  .Paginated()
                  .Items(allStreams)
                  .PageSize(12)
                  .CurrentPage(page)
                  .Page((elements, cur) =>
                  {
                      if (elements.Count == 0)
                          return CreateEmbed().WithDescription(GetText(strs.streams_none)).WithErrorColor();

                      var eb = CreateEmbed().WithTitle(GetText(strs.streams_follow_title)).WithOkColor();
                      for (var index = 0; index < elements.Count; index++)
                      {
                          var elem = elements[index];
                          eb.AddField($"**#{index + 1 + (12 * cur)}** {elem.Username.ToLower()}",
                              $"【{elem.Type}】\n<#{elem.ChannelId}>\n{elem.Message?.TrimTo(50)}",
                              true);
                      }

                      return eb;
                  })
                  .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        public async Task StreamOffline()
        {
            var newValue = await _service.ToggleStreamOffline(ctx.Guild.Id);
            if (newValue)
                await Response().Confirm(strs.stream_off_enabled).SendAsync();
            else
                await Response().Confirm(strs.stream_off_disabled).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        public async Task StreamOnlineDelete()
        {
            var newValue = await _service.ToggleStreamOnlineDelete(ctx.Guild.Id);
            if (newValue)
                await Response().Confirm(strs.stream_online_delete_enabled).SendAsync();
            else
                await Response().Confirm(strs.stream_online_delete_disabled).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        public async Task StreamMessage(int index, [Leftover] string message)
        {
            if (--index < 0)
                return;

            var canMentionEveryone = (ctx.User as IGuildUser)?.GuildPermissions.MentionEveryone ?? true;
            if (!canMentionEveryone)
                message = message?.SanitizeAllMentions();

            if (!_service.SetStreamMessage(ctx.Guild.Id, index, message, out var fs))
            {
                await Response().Confirm(strs.stream_not_following).SendAsync();
                return;
            }

            if (string.IsNullOrWhiteSpace(message))
                await Response().Confirm(strs.stream_message_reset(Format.Bold(fs.Username))).SendAsync();
            else
                await Response().Confirm(strs.stream_message_set(Format.Bold(fs.Username))).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        public async Task StreamMessageAll([Leftover] string message)
        {
            var canMentionEveryone = (ctx.User as IGuildUser)?.GuildPermissions.MentionEveryone ?? true;
            if (!canMentionEveryone)
                message = message?.SanitizeAllMentions();
            
            var count = _service.SetStreamMessageForAll(ctx.Guild.Id, message);

            if (count == 0)
            {
                await Response().Confirm(strs.stream_not_following_any).SendAsync();
                return;
            }

            await Response().Confirm(strs.stream_message_set_all(count)).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task StreamCheck(string url)
        {
            try
            {
                var data = await _service.GetStreamDataAsync(url);
                if (data is null)
                {
                    await Response().Error(strs.no_channel_found).SendAsync();
                    return;
                }

                if (data.IsLive)
                {
                    await Response()
                          .Confirm(strs.streamer_online(Format.Bold(data.Name),
                              Format.Bold(data.Viewers.ToString())))
                          .SendAsync();
                }
                else
                    await Response().Confirm(strs.streamer_offline(data.Name)).SendAsync();
            }
            catch
            {
                await Response().Error(strs.no_channel_found).SendAsync();
            }
        }
    }
}