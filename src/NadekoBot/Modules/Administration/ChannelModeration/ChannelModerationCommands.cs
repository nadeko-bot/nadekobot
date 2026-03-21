#nullable disable
using NadekoBot.Common.TypeReaders.Models;
using NadekoBot.Modules.Administration.Services;

namespace NadekoBot.Modules.Administration;

public enum List
{
    List = 0,
    Ls = 0
}

public enum Server
{
    Server
}

public enum _Channel
{
    Channel,
    Ch,
    Chnl,
    Chan
}

public enum State
{
    Enable,
    Disable,
    Inherit
}

public partial class Administration
{
    [Group]
    public partial class ChannelModerationCommands(
        SomethingOnlyChannelService somethingOnly,
        AdministrationService adminService) : NadekoModule
    {
        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [BotPerm(GuildPerm.ManageGuild)]
        public async Task ImageOnlyChannel(ParsedTimespan timespan = null)
        {
            var newValue = await somethingOnly.ToggleImageOnlyChannelAsync(ctx.Guild.Id, ctx.Channel.Id);
            if (newValue)
                await Response().Confirm(strs.imageonly_enable).SendAsync();
            else
                await Response().Pending(strs.imageonly_disable).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [BotPerm(GuildPerm.ManageGuild)]
        public async Task LinkOnlyChannel(ParsedTimespan timespan = null)
        {
            var newValue = await somethingOnly.ToggleLinkOnlyChannelAsync(ctx.Guild.Id, ctx.Channel.Id);
            if (newValue)
                await Response().Confirm(strs.linkonly_enable).SendAsync();
            else
                await Response().Pending(strs.linkonly_disable).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(ChannelPerm.ManageChannels)]
        [BotPerm(ChannelPerm.ManageChannels)]
        public async Task Slowmode(ParsedTimespan timespan = null)
        {
            var seconds = (int?)timespan?.Time.TotalSeconds ?? 0;
            if (timespan is not null && (timespan.Time < TimeSpan.FromSeconds(0) || timespan.Time > TimeSpan.FromHours(6)))
                return;

            await ((ITextChannel)ctx.Channel).ModifyAsync(tcp =>
            {
                tcp.SlowModeInterval = seconds;
            });

            await ctx.OkAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageChannels)]
        [BotPerm(GuildPerm.ManageChannels)]
        public async Task AgeRestrictToggle()
        {
            var channel = (ITextChannel)ctx.Channel;
            var isEnabled = channel.IsNsfw;

            await channel.ModifyAsync(c => c.IsNsfw = !isEnabled);

            if (isEnabled)
                await Response().Confirm(strs.nsfw_set_false).SendAsync();
            else
                await Response().Confirm(strs.nsfw_set_true).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [BotPerm(GuildPerm.ManageMessages)]
        [Priority(2)]
        public async Task Delmsgoncmd(List _)
        {
            var guild = (SocketGuild)ctx.Guild;
            var (enabled, channels) = await adminService.GetDelMsgOnCmdData(ctx.Guild.Id);

            var embed = CreateEmbed()
                .WithOkColor()
                .WithTitle(GetText(strs.server_delmsgoncmd))
                .WithDescription(enabled ? "✅" : "❌");

            var str = string.Join("\n",
                channels.Select(x =>
                {
                    var ch = guild.GetChannel(x.ChannelId)?.ToString() ?? x.ChannelId.ToString();
                    var prefixSign = x.State ? "✅ " : "❌ ";
                    return prefixSign + ch;
                }));

            if (string.IsNullOrWhiteSpace(str))
                str = "-";

            embed.AddField(GetText(strs.channel_delmsgoncmd), str);

            await Response().Embed(embed).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [BotPerm(GuildPerm.ManageMessages)]
        [Priority(1)]
        public async Task Delmsgoncmd(Server _ = Server.Server)
        {
            var enabled = await adminService.ToggleDelMsgOnCmd(ctx.Guild.Id);
            if (enabled)
            {
                await Response().Confirm(strs.delmsg_on).SendAsync();
            }
            else
            {
                await Response().Confirm(strs.delmsg_off).SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [BotPerm(GuildPerm.ManageMessages)]
        [Priority(0)]
        public Task Delmsgoncmd(_Channel _, State s, ITextChannel ch)
            => Delmsgoncmd(_, s, ch.Id);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [BotPerm(GuildPerm.ManageMessages)]
        [Priority(1)]
        public async Task Delmsgoncmd(_Channel _, State s, ulong? chId = null)
        {
            var actualChId = chId ?? ctx.Channel.Id;
            await adminService.SetDelMsgOnCmdState(ctx.Guild.Id, actualChId, s);

            if (s == State.Disable)
                await Response().Confirm(strs.delmsg_channel_off).SendAsync();
            else if (s == State.Enable)
                await Response().Confirm(strs.delmsg_channel_on).SendAsync();
            else
                await Response().Confirm(strs.delmsg_channel_inherit).SendAsync();
        }
    }
}
