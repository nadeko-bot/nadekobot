#nullable disable
namespace NadekoBot.Modules.Utility;

public partial class Utility
{
    [Group]
    public partial class NicknameCommands : NadekoModule
    {
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
    }
}
