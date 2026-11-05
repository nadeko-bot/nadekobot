namespace NadekoBot.Modules.Administration;

public partial class Administration
{
    [Group]
    [NoAiAgent]
    public partial class BotProfileCommands : NadekoModule<BotProfileService>
    {
        [Cmd]
        [BotProfilePerm]
        public Task SetAvatar()
            => SetImageInternalAsync(ProfileImage.Avatar);

        [Cmd]
        [BotProfilePerm]
        public Task SetBanner()
            => SetImageInternalAsync(ProfileImage.Banner);

        [Cmd]
        [BotProfilePerm]
        public async Task SetName([Leftover] string? newName = null)
        {
            if (ctx.Guild is null)
            {
                if (string.IsNullOrWhiteSpace(newName))
                {
                    await Response().Error(strs.bot_profile_no_name).SendAsync();
                    return;
                }

                await ReplyInternalAsync(await _service.SetGlobalNameAsync(newName),
                    strs.bot_name(Format.Bold(newName.Trim())));
                return;
            }

            var change = await _service.SetServerNicknameAsync(ctx.Guild, newName);
            await ReplyInternalAsync(change,
                string.IsNullOrWhiteSpace(newName)
                    ? strs.bot_nick_reset
                    : strs.bot_nick(Format.Bold(newName.Trim())));
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [BotProfilePerm]
        public async Task SetBio([Leftover] string? bio = null)
        {
            var change = await _service.SetServerBioAsync(ctx.Guild, bio);
            await ReplyInternalAsync(change,
                string.IsNullOrWhiteSpace(bio) ? strs.bot_bio_cleared : strs.bot_bio_set);
        }

        private async Task SetImageInternalAsync(ProfileImage kind)
        {
            var attachment = ctx.Message.Attachments.FirstOrDefault();
            if (ctx.Guild is null)
            {
                await ReplyInternalAsync(await _service.SetGlobalImageAsync(attachment, kind),
                    kind == ProfileImage.Avatar ? strs.set_avatar : strs.set_banner);
                return;
            }

            var change = await _service.SetServerImageAsync(ctx.Guild, attachment, kind);
            var success = (kind, attachment is null) switch
            {
                (ProfileImage.Avatar, true) => strs.bot_avatar_reset,
                (ProfileImage.Avatar, false) => strs.set_avatar,
                (_, true) => strs.bot_banner_reset,
                _ => strs.set_banner
            };

            await ReplyInternalAsync(change, success);
        }

        private async Task ReplyInternalAsync(ProfileChange change, LocStr success)
        {
            if (change.Status == ProfileStatus.Success)
            {
                await Response().Confirm(success).SendAsync();
                return;
            }

            await Response()
                  .Error(change.Status switch
                  {
                      ProfileStatus.NoImage => strs.bot_profile_no_image,
                      ProfileStatus.BadImage => strs.bot_profile_bad_image(BotProfileService.MAX_IMAGE_MB),
                      ProfileStatus.TooLong => strs.bot_profile_too_long(change.Value),
                      ProfileStatus.Wait => strs.bot_profile_wait(change.Value),
                      ProfileStatus.MissingPerm => strs.bot_profile_no_nick_perm,
                      ProfileStatus.RateLimited when ctx.Guild is not null => strs.bot_profile_wait(BotProfileService.SERVER_COOLDOWN_SECONDS),
                      _ => strs.bot_profile_refused
                  })
                  .SendAsync();
        }
    }
}
