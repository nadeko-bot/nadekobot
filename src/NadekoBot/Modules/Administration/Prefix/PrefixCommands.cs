#nullable disable
namespace NadekoBot.Modules.Administration;

public partial class Administration
{
    [Group]
    public partial class PrefixCommands : NadekoModule
    {
        public enum Set
        {
            Set
        }

        [Cmd]
        [Priority(1)]
        public async Task Prefix()
            => await Response().Confirm(strs.prefix_current(Format.Code(_cmdHandler.GetPrefix(ctx.Guild)))).SendAsync();

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [Priority(0)]
        public Task Prefix(Set _, [Leftover] string newPrefix)
            => Prefix(newPrefix);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [Priority(0)]
        public async Task Prefix([Leftover] string toSet)
        {
            if (string.IsNullOrWhiteSpace(prefix))
                return;

            var oldPrefix = prefix;
            var newPrefix = _cmdHandler.SetPrefix(ctx.Guild, toSet);

            await Response().Confirm(strs.prefix_new(Format.Code(oldPrefix), Format.Code(newPrefix))).SendAsync();
        }

        [Cmd]
        [OwnerOnly]
        public async Task DefPrefix([Leftover] string toSet = null)
        {
            if (string.IsNullOrWhiteSpace(toSet))
            {
                await Response().Confirm(strs.defprefix_current(_cmdHandler.GetPrefix())).SendAsync();
                return;
            }

            var oldPrefix = _cmdHandler.GetPrefix();
            var newPrefix = _cmdHandler.SetDefaultPrefix(toSet);

            await Response().Confirm(strs.defprefix_new(Format.Code(oldPrefix), Format.Code(newPrefix))).SendAsync();
        }
    }
}