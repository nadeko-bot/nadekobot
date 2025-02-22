#nullable disable
using NadekoBot.Common.TypeReaders.Models;
using NadekoBot.Modules.Administration._common.results;
using NadekoBot.Modules.Administration.Services;

namespace NadekoBot.Modules.Administration;

public partial class Administration : NadekoModule<AdministrationService>
{
    public enum Channel
    {
        Channel,
        Ch,
        Chnl,
        Chan
    }

    public enum List
    {
        List = 0,
        Ls = 0
    }

    public enum Server
    {
        Server
    }

    public enum State
    {
        Enable,
        Disable,
        Inherit
    }

    private readonly SomethingOnlyChannelService _somethingOnly;
    private readonly AutoPublishService _autoPubService;

    public Administration(SomethingOnlyChannelService somethingOnly, AutoPublishService autoPubService)
    {
        _somethingOnly = somethingOnly;
        _autoPubService = autoPubService;
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [UserPerm(GuildPerm.Administrator)]
    [BotPerm(GuildPerm.ManageGuild)]
    public async Task ImageOnlyChannel(ParsedTimespan timespan = null)
    {
        var newValue = await _somethingOnly.ToggleImageOnlyChannelAsync(ctx.Guild.Id, ctx.Channel.Id);
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
        var newValue = await _somethingOnly.ToggleLinkOnlyChannelAsync(ctx.Guild.Id, ctx.Channel.Id);
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
    [UserPerm(GuildPerm.Administrator)]
    [BotPerm(GuildPerm.ManageMessages)]
    [Priority(2)]
    public async Task Delmsgoncmd(List _)
    {
        var guild = (SocketGuild)ctx.Guild;
        var (enabled, channels) = await _service.GetDelMsgOnCmdData(ctx.Guild.Id);

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
        var enabled = await _service.ToggleDeleteMessageOnCommand(ctx.Guild.Id);
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
    public Task Delmsgoncmd(Channel _, State s, ITextChannel ch)
        => Delmsgoncmd(_, s, ch.Id);

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [UserPerm(GuildPerm.Administrator)]
    [BotPerm(GuildPerm.ManageMessages)]
    [Priority(1)]
    public async Task Delmsgoncmd(Channel _, State s, ulong? chId = null)
    {
        var actualChId = chId ?? ctx.Channel.Id;
        await _service.SetDelMsgOnCmdState(ctx.Guild.Id, actualChId, s);

        if (s == State.Disable)
            await Response().Confirm(strs.delmsg_channel_off).SendAsync();
        else if (s == State.Enable)
            await Response().Confirm(strs.delmsg_channel_on).SendAsync();
        else
            await Response().Confirm(strs.delmsg_channel_inherit).SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [UserPerm(GuildPerm.DeafenMembers)]
    [BotPerm(GuildPerm.DeafenMembers)]
    public async Task Deafen(params IGuildUser[] users)
    {
        await _service.DeafenUsers(true, users);
        await Response().Confirm(strs.deafen).SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [UserPerm(GuildPerm.DeafenMembers)]
    [BotPerm(GuildPerm.DeafenMembers)]
    public async Task UnDeafen(params IGuildUser[] users)
    {
        await _service.DeafenUsers(false, users);
        await Response().Confirm(strs.undeafen).SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [UserPerm(GuildPerm.ManageChannels)]
    [BotPerm(GuildPerm.ManageChannels)]
    public async Task DelVoiChanl([Leftover] IVoiceChannel voiceChannel)
    {
        await voiceChannel.DeleteAsync();
        await Response().Confirm(strs.delvoich(Format.Bold(voiceChannel.Name))).SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [UserPerm(GuildPerm.ManageChannels)]
    [BotPerm(GuildPerm.ManageChannels)]
    public async Task CreatVoiChanl([Leftover] string channelName)
    {
        var ch = await ctx.Guild.CreateVoiceChannelAsync(channelName);
        await Response().Confirm(strs.createvoich(Format.Bold(ch.Name))).SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [UserPerm(GuildPerm.ManageChannels)]
    [BotPerm(GuildPerm.ManageChannels)]
    public async Task DelTxtChanl([Leftover] ITextChannel toDelete)
    {
        await toDelete.DeleteAsync(new RequestOptions()
        {
            AuditLogReason = $"Deleted by {ctx.User.Username}"
        });
        await Response().Confirm(strs.deltextchan(Format.Bold(toDelete.Name))).SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [UserPerm(GuildPerm.ManageChannels)]
    [BotPerm(GuildPerm.ManageChannels)]
    public async Task CreaTxtChanl([Leftover] string channelName)
    {
        var txtCh = await ctx.Guild.CreateTextChannelAsync(channelName);
        await Response().Confirm(strs.createtextchan(Format.Bold(txtCh.Name))).SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [UserPerm(GuildPerm.ManageChannels)]
    [BotPerm(GuildPerm.ManageChannels)]
    public async Task SetTopic([Leftover] string topic = null)
    {
        var channel = (ITextChannel)ctx.Channel;
        topic ??= "";
        await channel.ModifyAsync(c => c.Topic = topic);
        await Response().Confirm(strs.set_topic).SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [UserPerm(GuildPerm.ManageChannels)]
    [BotPerm(GuildPerm.ManageChannels)]
    public async Task SetChanlName([Leftover] string name)
    {
        var channel = (ITextChannel)ctx.Channel;
        await channel.ModifyAsync(c => c.Name = name);
        await Response().Confirm(strs.set_channel_name).SendAsync();
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
    [UserPerm(ChannelPerm.ManageMessages)]
    [Priority(0)]
    public Task Edit(ulong messageId, [Leftover] string text)
        => Edit((ITextChannel)ctx.Channel, messageId, text);

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [Priority(1)]
    public async Task Edit(ITextChannel channel, ulong messageId, [Leftover] string text)
    {
        var userPerms = ((SocketGuildUser)ctx.User).GetPermissions(channel);
        var botPerms = ((SocketGuild)ctx.Guild).CurrentUser.GetPermissions(channel);
        if (!userPerms.Has(ChannelPermission.ManageMessages))
        {
            await Response().Error(strs.insuf_perms_u).SendAsync();
            return;
        }

        if (!botPerms.Has(ChannelPermission.ViewChannel))
        {
            await Response().Error(strs.insuf_perms_i).SendAsync();
            return;
        }

        await _service.EditMessage(ctx, channel, messageId, text);
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [UserPerm(ChannelPerm.ManageMessages)]
    [BotPerm(ChannelPerm.ManageMessages)]
    public Task Delete(ulong messageId, ParsedTimespan timespan = null)
        => Delete((ITextChannel)ctx.Channel, messageId, timespan);

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async Task Delete(MessageLink messageLink, ParsedTimespan timespan = null)
    {
        if (messageLink.Channel is not ITextChannel tc)
            return;

        await Delete(tc, messageLink.Message.Id, timespan);
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async Task Delete(ITextChannel channel, ulong messageId, ParsedTimespan timespan = null)
        => await InternalMessageAction(channel, messageId, timespan, msg => msg.DeleteAsync());

    private async Task InternalMessageAction(
        ITextChannel channel,
        ulong messageId,
        ParsedTimespan timespan,
        Func<IMessage, Task> func)
    {
        var userPerms = ((SocketGuildUser)ctx.User).GetPermissions(channel);
        var botPerms = ((SocketGuild)ctx.Guild).CurrentUser.GetPermissions(channel);
        if (!userPerms.Has(ChannelPermission.ManageMessages))
        {
            await Response().Error(strs.insuf_perms_u).SendAsync();
            return;
        }

        if (!botPerms.Has(ChannelPermission.ManageMessages))
        {
            await Response().Error(strs.insuf_perms_i).SendAsync();
            return;
        }


        var msg = await channel.GetMessageAsync(messageId);
        if (msg is null)
        {
            await Response().Error(strs.msg_not_found).SendAsync();
            return;
        }

        if (timespan is null)
            await msg.DeleteAsync();
        else if (timespan.Time <= TimeSpan.FromDays(7))
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(timespan.Time);
                await msg.DeleteAsync();
            });
        }
        else
        {
            await Response().Error(strs.time_too_long).SendAsync();
            return;
        }

        await ctx.OkAsync();
    }

    [Cmd]
    [BotPerm(ChannelPermission.CreatePublicThreads)]
    [UserPerm(ChannelPermission.CreatePublicThreads)]
    public async Task ThreadCreate([Leftover] string name)
    {
        if (ctx.Channel is not SocketTextChannel stc)
            return;

        await stc.CreateThreadAsync(name, message: ctx.Message.ReferencedMessage);
        await ctx.OkAsync();
    }

    [Cmd]
    [BotPerm(ChannelPermission.ManageThreads)]
    [UserPerm(ChannelPermission.ManageThreads)]
    public async Task ThreadDelete([Leftover] string name)
    {
        if (ctx.Channel is not SocketTextChannel stc)
            return;

        var t = stc.Threads.FirstOrDefault(
            x => string.Equals(x.Name, name, StringComparison.InvariantCultureIgnoreCase));

        if (t is null)
        {
            await Response().Error(strs.not_found).SendAsync();
            return;
        }

        await t.DeleteAsync();
        await ctx.OkAsync();
    }

    [Cmd]
    [UserPerm(ChannelPerm.ManageMessages)]
    public async Task AutoPublish()
    {
        if (ctx.Channel.GetChannelType() != ChannelType.News)
        {
            await Response().Error(strs.req_announcement_channel).SendAsync();
            return;
        }

        var newState = await _autoPubService.ToggleAutoPublish(ctx.Guild.Id, ctx.Channel.Id);

        if (newState)
        {
            await Response().Confirm(strs.autopublish_enable).SendAsync();
        }
        else
        {
            await Response().Confirm(strs.autopublish_disable).SendAsync();
        }
    }

    [Cmd]
    [UserPerm(GuildPerm.ManageNicknames)]
    [BotPerm(GuildPerm.ChangeNickname)]
    [Priority(0)]
    public async Task SetNick([Leftover] string newNick = null)
    {
        if (string.IsNullOrWhiteSpace(newNick))
            return;
        var curUser = await ctx.Guild.GetCurrentUserAsync();
        await curUser.ModifyAsync(u => u.Nickname = newNick);

        await Response().Confirm(strs.bot_nick(Format.Bold(newNick) ?? "-")).SendAsync();
    }

    [Cmd]
    [BotPerm(GuildPerm.ManageNicknames)]
    [UserPerm(GuildPerm.ManageNicknames)]
    [Priority(1)]
    public async Task SetNick(IGuildUser gu, [Leftover] string newNick = null)
    {
        var sg = (SocketGuild)ctx.Guild;
        if (sg.OwnerId == gu.Id
            || gu.GetRoles().Max(r => r.Position) >= sg.CurrentUser.GetRoles().Max(r => r.Position))
        {
            await Response().Error(strs.insuf_perms_i).SendAsync();
            return;
        }

        await gu.ModifyAsync(u => u.Nickname = newNick);

        await Response()
              .Confirm(strs.user_nick(Format.Bold(gu.ToString()), Format.Bold(newNick) ?? "-"))
              .SendAsync();
    }


    [Cmd]
    [RequireContext(ContextType.Guild)]
    [UserPerm(GuildPermission.ManageGuild)]
    [BotPerm(GuildPermission.ManageGuild)]
    public async Task SetServerBanner([Leftover] string img = null)
    {
        // Tier2 or higher is required to set a banner.
        if (ctx.Guild.PremiumTier is PremiumTier.Tier1 or PremiumTier.None)
            return;

        var result = await _service.SetServerBannerAsync(ctx.Guild, img);

        switch (result)
        {
            case SetServerBannerResult.Success:
                await Response().Confirm(strs.set_srvr_banner).SendAsync();
                break;
            case SetServerBannerResult.InvalidFileType:
                await Response().Error(strs.srvr_banner_invalid).SendAsync();
                break;
            case SetServerBannerResult.Toolarge:
                await Response().Error(strs.srvr_banner_too_large).SendAsync();
                break;
            case SetServerBannerResult.InvalidURL:
                await Response().Error(strs.srvr_banner_invalid_url).SendAsync();
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [UserPerm(GuildPermission.ManageGuild)]
    [BotPerm(GuildPermission.ManageGuild)]
    public async Task SetServerIcon([Leftover] string img = null)
    {
        var result = await _service.SetServerIconAsync(ctx.Guild, img);

        switch (result)
        {
            case SetServerIconResult.Success:
                await Response().Confirm(strs.set_srvr_icon).SendAsync();
                break;
            case SetServerIconResult.InvalidFileType:
                await Response().Error(strs.srvr_banner_invalid).SendAsync();
                break;
            case SetServerIconResult.InvalidURL:
                await Response().Error(strs.srvr_banner_invalid_url).SendAsync();
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}