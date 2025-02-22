using NadekoBot.Modules.Administration.Honeypot;

namespace NadekoBot.Modules.Administration;

public partial class Administration
{
    [Group]
    public partial class HoneypotCommands : NadekoModule
    {
        private readonly IHoneyPotService _service;

        public HoneypotCommands(IHoneyPotService service)
            => _service = service;

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.Administrator)]
        [RequireBotPermission(GuildPermission.BanMembers)]
        public async Task Honeypot()
        {
            var enabled = await _service.ToggleHoneypotChannel(ctx.Guild.Id, ctx.Channel.Id);

            if (enabled)
                await Response().Confirm(strs.honeypot_on).SendAsync();
            else
                await Response().Confirm(strs.honeypot_off).SendAsync();
        }
    }
}