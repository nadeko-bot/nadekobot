#nullable disable
using Musix.Models;
using NadekoBot.Modules.Games.Services;
using NadekoBot.Modules.Music;

namespace NadekoBot.Modules.Games;

public partial class Games
{
    [Group]
    public partial class ChatterBotCommands : NadekoModule<ChatterBotService>
    {
        private readonly DbService _db;

        public ChatterBotCommands(DbService db)
            => _db = db;

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        public async Task CleverBot()
        {
            var channel = (ITextChannel)ctx.Channel;

            var newState = await _service.ToggleChatterBotAsync(ctx.Guild.Id);

            if (!newState)
            {
                await Response().Confirm(strs.chatbot_disabled).SendAsync();
                return;
            }

            await Response().Confirm(strs.chatbot_enabled).SendAsync();

        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        public async Task ResetChatBotSession()
        {
            if (_service.ResetChatterBot(ctx.Guild.Id))
            {
                await Response().Confirm(strs.chatbot_reset).SendAsync();
            }
            else
            {
                await Response().Confirm(strs.chatbot_reset_failed).SendAsync();
            }
        }
    }
}