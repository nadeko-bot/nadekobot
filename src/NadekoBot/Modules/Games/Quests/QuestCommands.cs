using NadekoBot.Modules.Games.Quests;

namespace NadekoBot.Modules.Games;

public partial class Games
{
    public class QuestCommands : NadekoModule<QuestLogService>
    {
        [Cmd]
        public async Task QuestLog()
        {
            var embed = await _service.BuildEmbedAsync(ctx.User.Id, ctx.Guild?.Id);
            await Response().Embed(embed).SendAsync();
        }
    }
}