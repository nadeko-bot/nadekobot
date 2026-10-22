using NadekoBot.Modules.Owner.UserData;

namespace NadekoBot.Modules.Owner;

public partial class Owner
{
    [Group]
    [OwnerOnly]
    public partial class UserDataCommands : NadekoModule<UserPurgeService>
    {
        [Cmd]
        [OwnerOnly]
        public async Task PurgeUser(ulong userId)
        {
            var embed = CreateEmbed()
                .WithDescription(GetText(strs.purge_user_confirm(Format.Bold(userId.ToString()))));

            if (!await PromptUserConfirmAsync(embed))
                return;

            var changed = await _service.PurgeAsync(userId);
            await Response().Confirm(strs.purge_user_done(Format.Bold(userId.ToString()), changed)).SendAsync();
        }

        [Cmd]
        [OwnerOnly]
        public Task PurgeUser([Leftover] IUser user)
            => PurgeUser(user.Id);
    }
}
