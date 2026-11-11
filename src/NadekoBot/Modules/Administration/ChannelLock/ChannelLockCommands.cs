using System.Net;
using NadekoBot.Modules.Administration.Services;

namespace NadekoBot.Modules.Administration;

public partial class Administration
{
    [Group]
    public partial class ChannelLockCommands(ChannelLockService cls) : NadekoModule
    {
        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageChannels)]
        [BotPerm(GuildPerm.ManageRoles)]
        public async Task Lock(ITextChannel? channel = null)
        {
            channel ??= (ITextChannel)ctx.Channel;
            if (!await CheckChannelInternalAsync(channel))
                return;

            if (ChannelLockService.IsLocked(channel))
            {
                await Response().Error(strs.lock_already(channel.Mention)).SendAsync();
                return;
            }

            // the bot posts the notice first, because the lock can take away its own Send Messages
            var notice = await TrySendNoticeInternalAsync(channel, strs.lock_notice);

            ChannelLockResult result;
            try
            {
                result = await cls.LockAsync(channel);
            }
            catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
            {
                await TryDeleteInternalAsync(notice);
                await Response().Error(strs.lock_bot_perms(channel.Mention)).SendAsync();
                return;
            }
            catch
            {
                await TryDeleteInternalAsync(notice);
                throw;
            }

            if (result == ChannelLockResult.AlreadyLocked)
            {
                await TryDeleteInternalAsync(notice);
                await Response().Error(strs.lock_already(channel.Mention)).SendAsync();
                return;
            }

            if (channel.Id == ctx.Channel.Id)
                await ctx.OkAsync();
            else
                await Response().Confirm(strs.lock_done(channel.Mention)).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageChannels)]
        [BotPerm(GuildPerm.ManageRoles)]
        public async Task Unlock(ITextChannel? channel = null)
        {
            channel ??= (ITextChannel)ctx.Channel;
            if (!await CheckChannelInternalAsync(channel))
                return;

            ChannelLockResult result;
            try
            {
                result = await cls.UnlockAsync(channel);
            }
            catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
            {
                await Response().Error(strs.lock_bot_perms(channel.Mention)).SendAsync();
                return;
            }

            if (result == ChannelLockResult.NotLocked)
            {
                await Response().Error(strs.unlock_not_locked(channel.Mention)).SendAsync();
                return;
            }

            var notice = await TrySendNoticeInternalAsync(channel, strs.unlock_notice, afterUnlock: true);

            if (channel.Id != ctx.Channel.Id)
                await Response().Confirm(strs.unlock_done(channel.Mention)).SendAsync();
            else if (notice is null)
                await ctx.OkAsync();
        }

        private async Task<bool> CheckChannelInternalAsync(ITextChannel channel)
        {
            if (channel is IThreadChannel)
            {
                await Response().Error(strs.lock_no_threads).SendAsync();
                return false;
            }

            if (!((IGuildUser)ctx.User).GetPermissions(channel).Has(ChannelPermission.ManageChannels))
            {
                await Response().Error(strs.insuf_perms_u).SendAsync();
                return false;
            }

            var bot = await ctx.Guild.GetCurrentUserAsync();
            if ((bot.GetPermissions(channel).RawValue & ChannelLockService.BOT_CHANNEL_MASK)
                != ChannelLockService.BOT_CHANNEL_MASK)
            {
                await Response().Error(strs.lock_bot_perms(channel.Mention)).SendAsync();
                return false;
            }

            return true;
        }

        private async Task<IUserMessage?> TrySendNoticeInternalAsync(
            ITextChannel channel,
            LocStr text,
            bool afterUnlock = false)
        {
            var bot = await ctx.Guild.GetCurrentUserAsync();
            var perms = bot.GetPermissions(channel);

            // the cached overwrite still has the lock right after an unlock, so the server permission decides
            var canSend = afterUnlock ? bot.GuildPermissions.SendMessages : perms.SendMessages;
            if (!canSend || !perms.ViewChannel || !perms.EmbedLinks)
                return null;

            // the notice is optional, so no error of it stops the lock or the unlock
            try
            {
                return await _sender.Response(channel).Pending(text).SendAsync();
            }
            catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
            {
                return null;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Unable to send the lock notice in channel {ChannelId}", channel.Id);
                return null;
            }
        }

        private static async Task TryDeleteInternalAsync(IUserMessage? msg)
        {
            if (msg is null)
                return;

            try
            {
                await msg.DeleteAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Unable to delete the lock notice {MessageId}", msg.Id);
            }
        }
    }
}
