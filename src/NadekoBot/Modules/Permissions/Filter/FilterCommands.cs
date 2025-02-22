#nullable disable
using Microsoft.EntityFrameworkCore;
using NadekoBot.Modules.Permissions.Services;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Permissions;

public partial class Permissions
{
    [Group]
    public partial class FilterCommands : NadekoModule<FilterService>
    {
        private readonly DbService _db;

        public FilterCommands(DbService db)
            => _db = db;

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task FwClear()
        {
            await _service.ClearFilteredWords(ctx.Guild.Id);
            await Response().Confirm(strs.fw_cleared).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task FilterList()
        {
            var embed = CreateEmbed()
                        .WithOkColor()
                        .WithTitle("Server filter settings");

            var config = await _service.GetFilterSettings(ctx.Guild.Id);

            string GetEnabledEmoji(bool value)
                => value ? "\\🟢" : "\\🔴";

            async Task<string> GetChannelListAsync(IReadOnlyCollection<ulong> channels)
            {
                var toReturn = (await channels
                                      .Select(async cid =>
                                      {
                                          var ch = await ctx.Guild.GetChannelAsync(cid);
                                          return ch is null
                                              ? $"{cid} *missing*"
                                              : $"<#{cid}>";
                                      })
                                      .WhenAll())
                    .Join('\n');

                if (string.IsNullOrWhiteSpace(toReturn))
                    return GetText(strs.no_channel_found);

                return toReturn;
            }

            embed.AddField($"{GetEnabledEmoji(config.FilterLinksEnabled)} Filter Links",
                await GetChannelListAsync(config.FilterLinksChannels));

            embed.AddField($"{GetEnabledEmoji(config.FilterInvitesEnabled)} Filter Invites",
                await GetChannelListAsync(config.FilterInvitesChannels));

            await Response().Embed(embed).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task SrvrFilterInv()
        {
            var channel = (ITextChannel)ctx.Channel;

            var enabled = await _service.ToggleServerInviteFilteringAsync(channel.Guild.Id);

            if (enabled)
            {
                await Response().Confirm(strs.invite_filter_server_on).SendAsync();
            }
            else
            {
                await Response().Confirm(strs.invite_filter_server_off).SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task ChnlFilterInv()
        {
            var channel = (ITextChannel)ctx.Channel;

            var enabled = await _service.ToggleChannelInviteFilteringAsync(channel.Guild.Id, channel.Id);

            if (enabled)
            {
                await Response().Confirm(strs.invite_filter_channel_on).SendAsync();
            }
            else
            {
                await Response().Confirm(strs.invite_filter_channel_off).SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task SrvrFilterLin()
        {
            var channel = (ITextChannel)ctx.Channel;

            var enabled = await _service.ToggleServerLinkFilteringAsync(channel.Guild.Id);

            if (enabled)
            {
                await Response().Confirm(strs.link_filter_server_on).SendAsync();
            }
            else
            {
                await Response().Confirm(strs.link_filter_server_off).SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task ChnlFilterLin()
        {
            var channel = (ITextChannel)ctx.Channel;

            var enabled = await _service.ToggleChannelLinkFilteringAsync(channel.Guild.Id, channel.Id);

            if (enabled)
            {
                await Response().Confirm(strs.link_filter_channel_on).SendAsync();
            }
            else
            {
                await Response().Confirm(strs.link_filter_channel_off).SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task SrvrFilterWords()
        {
            var channel = (ITextChannel)ctx.Channel;

            var enabled = await _service.ToggleServerWordFilteringAsync(channel.Guild.Id);

            if (enabled)
            {
                await Response().Confirm(strs.word_filter_server_on).SendAsync();
            }
            else
            {
                await Response().Confirm(strs.word_filter_server_off).SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task ChnlFilterWords()
        {
            var channel = (ITextChannel)ctx.Channel;

            var enabled = await _service.ToggleChannelWordFilteringAsync(channel.Guild.Id, channel.Id);

            if (enabled)
            {
                await Response().Confirm(strs.word_filter_channel_on).SendAsync();
            }
            else
            {
                await Response().Confirm(strs.word_filter_channel_off).SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task FilterWord([Leftover] string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return;

            var enabled = await _service.ToggleFilteredWordAsync(ctx.Guild.Id, word);

            if (enabled)
            {
                await Response().Confirm(strs.filter_word_add(Format.Code(word))).SendAsync();
            }
            else
            {
                await Response().Confirm(strs.filter_word_remove(Format.Code(word))).SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task LstFilterWords(int page = 1)
        {
            page--;
            if (page < 0)
                return;

            var fws = _service.FilteredWordsForServer(ctx.Guild.Id);

            await Response()
                  .Paginated()
                  .Items(fws)
                  .PageSize(10)
                  .CurrentPage(page)
                  .Page((items, _) => CreateEmbed()
                                      .WithTitle(GetText(strs.filter_word_list))
                                      .WithDescription(string.Join("\n", items))
                                      .WithOkColor())
                  .SendAsync();
        }
    }
}