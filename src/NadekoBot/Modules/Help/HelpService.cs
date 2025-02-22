using NadekoBot.Common.ModuleBehaviors;

namespace NadekoBot.Modules.Help.Services;

public class HelpService : IExecNoCommand, INService
{
    private readonly BotConfigService _bss;
    private readonly IReplacementService _rs;
    private readonly IMessageSenderService _sender;

    public HelpService(BotConfigService bss, IReplacementService repSvc, IMessageSenderService sender)
    {
        _bss = bss;
        _rs = repSvc;
        _sender = sender;
    }

    public async Task ExecOnNoCommandAsync(IGuild? guild, IUserMessage msg)
    {
        var settings = _bss.Data;
        if (guild is null)
        {
            if (string.IsNullOrWhiteSpace(settings.DmHelpText) || settings.DmHelpText == "-")
                return;

            // only send dm help text if it contains one of the keywords, if they're specified
            // if they're not, then reply to every DM
            if (settings.DmHelpTextKeywords is not null
                && !settings.DmHelpTextKeywords.Any(k => msg.Content.Contains(k)))
            {
                return;
            }

            var repCtx = new ReplacementContext(guild: guild, channel: msg.Channel, user: msg.Author)
                         .WithOverride("%prefix%", () => _bss.Data.Prefix)
                         .WithOverride("%bot.prefix%", () => _bss.Data.Prefix);

            var text = SmartText.CreateFrom(settings.DmHelpText);
            text = await _rs.ReplaceAsync(text, repCtx);

            await _sender.Response(msg.Channel).Text(text).SendAsync();
        }
    }
}