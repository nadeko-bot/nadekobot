#nullable disable
using NadekoBot.Modules.Gambling.Common;
using NadekoBot.Modules.Gambling.Common.Events;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Gambling.Services;

public sealed class CurrencyEventsService(
    DiscordSocketClient client,
    ICurrencyService cs,
    GamblingConfigService configService,
    IMessageSenderService sender) : INService
{
    private readonly ConcurrentDictionary<ulong, ICurrencyEvent> _events = new();

    public async Task<bool> TryCreateEventAsync(
        ulong guildId,
        ulong channelId,
        CurrencyEvent.Type type,
        EventOptions opts,
        Func<CurrencyEvent.Type, EventOptions, long, EmbedBuilder> embed)
    {
        if (type != CurrencyEvent.Type.Reaction)
            return false;

        var g = client.GetGuild(guildId);
        if (g?.GetChannel(channelId) is not ITextChannel ch)
            return false;

        ICurrencyEvent ce = new ReactionEvent(client, cs, g, ch, opts, configService.Data, sender, embed);

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