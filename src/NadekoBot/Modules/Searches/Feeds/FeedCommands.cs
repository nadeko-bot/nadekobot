using CodeHollow.FeedReader;
using NadekoBot.Modules.Searches.Services;
using System.Text.RegularExpressions;

namespace NadekoBot.Modules.Searches;

public partial class Searches
{
    [Group]
    public partial class FeedCommands : NadekoModule<FeedsService>
    {
        private static readonly Regex _ytChannelRegex =
            new(@"youtube\.com\/(?:c\/|channel\/|user\/)?(?<channelid>[a-zA-Z0-9\-_]{1,})");

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        [Priority(1)]
        public Task YtUploadNotif(string url, [Leftover] string? message = null)
            => YtUploadNotif(url, null, message);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        [Priority(2)]
        public Task YtUploadNotif(string url, ITextChannel? channel = null, [Leftover] string? message = null)
        {
            var m = _ytChannelRegex.Match(url);
            if (!m.Success)
                return Response().Error(strs.invalid_input).SendAsync();

            channel ??= ctx.Channel as ITextChannel;

            if (!((IGuildUser)ctx.User).GetPermissions(channel).MentionEveryone)
                message = message?.SanitizeAllMentions();

            var channelId = m.Groups["channelid"].Value;

            return Feed($"https://www.youtube.com/feeds/videos.xml?channel_id={channelId}", channel, message);
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        [Priority(0)]
        public Task Feed(string url, [Leftover] string? message = null)
            => Feed(url, null, message);


        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        [Priority(1)]
        public async Task Feed(string url, ITextChannel? channel = null, [Leftover] string? message = null)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                await Response().Error(strs.feed_invalid_url).SendAsync();
                return;
            }

            channel ??= (ITextChannel)ctx.Channel;
            
            if (!((IGuildUser)ctx.User).GetPermissions(channel).MentionEveryone)
                message = message?.SanitizeAllMentions();

            try
            {
                await FeedReader.ReadAsync(url);
            }
            catch (Exception ex)
            {
                Log.Information(ex, "Unable to get feeds from that url");
                await Response().Error(strs.feed_cant_parse).SendAsync();
                return;
            }

            if (ctx.User is not IGuildUser gu || !gu.GuildPermissions.Administrator)
                message = message?.SanitizeMentions(true);

            var result = _service.AddFeed(ctx.Guild.Id, channel.Id, url, message);
            if (result == FeedAddResult.Success)
            {
                await Response().Confirm(strs.feed_added).SendAsync();
                return;
            }

            if (result == FeedAddResult.Duplicate)
            {
                await Response().Error(strs.feed_duplicate).SendAsync();
                return;
            }

            if (result == FeedAddResult.LimitReached)
            {
                await Response().Error(strs.feed_limit_reached).SendAsync();
                return;
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        public async Task FeedRemove(int index)
        {
            if (_service.RemoveFeed(ctx.Guild.Id, --index))
                await Response().Confirm(strs.feed_removed).SendAsync();
            else
                await Response().Error(strs.feed_out_of_range).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        public async Task FeedList(int page = 1)
        {
            if (--page < 0)
                return;
            
            var feeds = _service.GetFeeds(ctx.Guild.Id);

            if (!feeds.Any())
            {
                await Response()
                      .Embed(CreateEmbed().WithOkColor().WithDescription(GetText(strs.feed_no_feed)))
                      .SendAsync();
                return;
            }

            await Response()
                  .Paginated()
                  .Items(feeds)
                  .PageSize(10)
                  .CurrentPage(page)
                  .Page((items, cur) =>
                  {
                      var embed = CreateEmbed().WithOkColor();
                      var i = 0;
                      var fs = string.Join("\n",
                          items.Select(x => $"`{(cur * 10) + ++i}.` <#{x.ChannelId}> {x.Url}"));

                      return embed.WithDescription(fs);
                  })
                  .SendAsync();
        }
    }
}