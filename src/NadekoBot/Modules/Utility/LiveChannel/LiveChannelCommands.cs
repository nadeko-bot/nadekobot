using NadekoBot.Modules.Utility.LiveChannel;

namespace NadekoBot.Modules.Utility;

public partial class Utility
{
    [Group]
    public class LiveChannelCommands(LiveChannelService svc) : NadekoModule
    {
        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageChannels)]
        [BotPerm(GuildPerm.ManageChannels)]
        public async Task LiveChAdd(IChannel channel, [Leftover] string template)
        {
            if (!await svc.AddLiveChannelAsync(ctx.Guild.Id, channel.Id, ctx.Guild.OwnerId, template))
            {
                await Response()
                    .Error(strs.livechannel_limit(await svc.GetMaxLiveChannels(ctx.Guild.OwnerId)))
                    .SendAsync();
                return;
            }

            var eb = CreateEmbed()
                .WithOkColor()
                .WithDescription(GetText(strs.livechannel_added(channel.Name)))
                .AddField(GetText(strs.template), template, true)
                .AddField(GetText(strs.preview),
                    await repSvc.ReplaceAsync(template,
                        new(
                            client: ctx.Client as DiscordSocketClient,
                            guild: ctx.Guild
                        )),
                    true)
                .WithFooter(GetText(strs.livechannel_please_wait));

            await Response()
                .Embed(eb)
                .SendAsync();
            return;
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageChannels)]
        [BotPerm(GuildPerm.ManageChannels)]
        public async Task LiveChList()
        {
            var liveChannels = await svc.GetLiveChannelsAsync(ctx.Guild.Id);

            if (liveChannels.Count == 0)
            {
                await Response().Pending(strs.livechannel_list_empty).SendAsync();
                return;
            }

            var embed = CreateEmbed()
                .WithTitle(GetText(strs.livechannel_list_title(ctx.Guild.Name)));

            foreach (var config in liveChannels)
            {
                var channelName = await ctx.Guild.GetChannelAsync(config.ChannelId)
                    .Fmap(x => x?.Name ?? config.ChannelId.ToString());

                embed.AddField(channelName, config.Template);
            }

            await Response().Embed(embed).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageChannels)]
        [BotPerm(GuildPerm.ManageChannels)]
        public Task LiveChRemove(IChannel channel)
            => LiveChRemove(channel.Id);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageChannels)]
        [BotPerm(GuildPerm.ManageChannels)]
        public async Task LiveChRemove(ulong channelId)
        {
            if (await svc.RemoveLiveChannelAsync(ctx.Guild.Id, channelId))
            {
                await Response()
                    .Confirm(strs.livechannel_removed(((SocketGuild)ctx.Guild).GetChannel(channelId)?.Name ??
                                                      channelId.ToString())).SendAsync();
            }
            else
            {
                await Response().Error(strs.livechannel_not_found).SendAsync();
            }
        }
    }
}