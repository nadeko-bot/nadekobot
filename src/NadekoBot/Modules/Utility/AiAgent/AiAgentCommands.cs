using NadekoBot.Modules.Utility.AiAgent;

namespace NadekoBot.Modules.Utility;

public partial class Utility
{
    [Group]
    public partial class AiAgentCommands : NadekoModule<AiAgent.AiAgentService>
    {
        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task Agent([Leftover] string query)
        {
            if (!await _service.IsAllowedAsync(ctx.User))
            {
                await Response().Error(strs.agent_not_allowed).SendAsync();
                return;
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                await Response().Error(strs.agent_no_query).SendAsync();
                return;
            }

            await _service.TryRunAgentAsync(ctx.Guild, (ITextChannel)ctx.Channel, ctx.Message, query);
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task AgentCancel()
        {
            if (!await _service.IsAllowedAsync(ctx.User))
            {
                await Response().Error(strs.agent_not_allowed).SendAsync();
                return;
            }

            if (_service.CancelSession(ctx.User.Id))
                await Response().Confirm(strs.agent_cancelled).SendAsync();
            else
                await Response().Error(strs.agent_no_session).SendAsync();
        }
    }
}
