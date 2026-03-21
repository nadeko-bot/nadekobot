#nullable disable
using NadekoBot.Common.TypeReaders.Models;
using NadekoBot.Modules.Administration.Services;

namespace NadekoBot.Modules.Administration;

public partial class Administration
{
    [Group]
    public partial class MessageCommands(
        AdministrationService service,
        AutoPublishService autoPubService) : NadekoModule<AdministrationService>
    {
        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(ChannelPerm.ManageMessages)]
        [Priority(0)]
        public async Task Edit(ulong messageId, [Leftover] string text)
            => await Edit((ITextChannel)ctx.Channel, messageId, text);

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

            await service.EditMessage(ctx, channel, messageId, text);
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(ChannelPerm.ManageMessages)]
        [BotPerm(ChannelPerm.ManageMessages)]
        public async Task Delete(ulong messageId, ParsedTimespan timespan = null)
            => await Delete((ITextChannel)ctx.Channel, messageId, timespan);

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
        [UserPerm(ChannelPerm.ManageMessages)]
        public async Task AutoPublish()
        {
            if (ctx.Channel.GetChannelType() != ChannelType.News)
            {
                await Response().Error(strs.req_announcement_channel).SendAsync();
                return;
            }

            var result = await autoPubService.ToggleAutoPublish(ctx.Guild.Id, ctx.Channel.Id);

            if (result)
            {
                await Response().Confirm(strs.autopublish_enable).SendAsync();
            }
            else
            {
                await Response().Confirm(strs.autopublish_disable).SendAsync();
            }
        }
    }
}
