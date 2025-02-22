#nullable disable
using NadekoBot.Modules.Gambling.Common;
using NadekoBot.Modules.Gambling.Common.Events;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Gambling.Services;

public class CurrencyEventsService : INService
{
    private readonly DiscordSocketClient _client;
    private readonly ICurrencyService _cs;
    private readonly GamblingConfigService _configService;

    private readonly ConcurrentDictionary<ulong, ICurrencyEvent> _events = new();
    private readonly IMessageSenderService _sender;

    public CurrencyEventsService(DiscordSocketClient client, ICurrencyService cs, GamblingConfigService configService,
        IMessageSenderService sender)
    {
        _client = client;
        _cs = cs;
        _configService = configService;
        _sender = sender;
    }

    public async Task<bool> TryCreateEventAsync(
        ulong guildId,
        ulong channelId,
        CurrencyEvent.Type type,
        EventOptions opts,
        Func<CurrencyEvent.Type, EventOptions, long, EmbedBuilder> embed)
    {
        var g = _client.GetGuild(guildId);
        if (g?.GetChannel(channelId) is not ITextChannel ch)
            return false;

        ICurrencyEvent ce;

        if (type == CurrencyEvent.Type.Reaction)
            ce = new ReactionEvent(_client, _cs, g, ch, opts, _configService.Data, _sender, embed);
        else if (type == CurrencyEvent.Type.GameStatus)
            ce = new GameStatusEvent(_client, _cs, g, ch, opts, _sender, embed);
        else
            return false;

        var added = _events.TryAdd(guildId, ce);
        if (added)
        {
            try
            {
                ce.OnEnded += OnEventEnded;
                await ce.StartEvent();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error starting event");
                _events.TryRemove(guildId, out ce);
                return false;
            }
        }

        return added;
    }

    private Task OnEventEnded(ulong gid)
    {
        _events.TryRemove(gid, out _);
        return Task.CompletedTask;
    }
}