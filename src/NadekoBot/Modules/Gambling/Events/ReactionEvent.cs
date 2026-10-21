#nullable disable
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Gambling.Common.Events;

public class ReactionEvent : ICurrencyEvent
{
    private const int MIN_ACCOUNT_AGE_DAYS = 5;

    public event Func<ulong, Task> OnEnded;
    public bool Stopped { get; private set; }
    private readonly DiscordSocketClient _client;
    private readonly IGuild _guild;
    private IUserMessage msg;
    private IEmote emote;

    private readonly Func<CurrencyEvent.Type, EventOptions, long, EmbedBuilder> _embedFunc;
    private readonly ITextChannel _channel;
    private readonly EventPayout _payout;
    private readonly CancellationTokenSource _cts = new();
    private readonly EventOptions _opts;
    private readonly GamblingConfig _config;

    private readonly Lock _stopLock = new();
    private readonly IMessageSenderService _sender;

    public ReactionEvent(
        DiscordSocketClient client,
        ICurrencyService cs,
        SocketGuild g,
        ITextChannel ch,
        EventOptions opt,
        GamblingConfig config,
        IMessageSenderService sender,
        Func<CurrencyEvent.Type, EventOptions, long, EmbedBuilder> embedFunc)
    {
        _client = client;
        _guild = g;
        _embedFunc = embedFunc;
        _channel = ch;
        _opts = opt;
        _config = config;
        _sender = sender;
        _payout = new(cs, opt.Amount, opt.PotSize, new("event", "reaction"));
    }

    public async Task StartEvent()
    {
        if (Emote.TryParse(_config.Currency.Sign, out var parsedEmote))
            emote = parsedEmote;
        else
            emote = new Emoji(_config.Currency.Sign);
        msg = await _sender.Response(_channel).Embed(GetEmbed(_opts.PotSize)).SendAsync();
        await msg.AddReactionAsync(emote);
        _client.MessageDeleted += OnMessageDeleted;
        _client.ReactionAdded += HandleReaction;
        _ = Task.Run(() => _payout.RunAsync(TimeSpan.FromHours(_opts.Hours),
            pot => msg.ModifyAsync(m => m.Embed = GetEmbed(pot).Build()),
            StopEvent,
            _cts.Token));
    }

    private EmbedBuilder GetEmbed(long pot)
        => _embedFunc(CurrencyEvent.Type.Reaction, _opts, pot);

    private Task OnMessageDeleted(Cacheable<IMessage, ulong> message, Cacheable<IMessageChannel, ulong> cacheable)
    {
        if (message.Id == msg.Id)
            return StopEvent();

        return Task.CompletedTask;
    }

    public Task StopEvent()
    {
        lock (_stopLock)
        {
            if (Stopped)
                return Task.CompletedTask;

            Stopped = true;
            _client.MessageDeleted -= OnMessageDeleted;
            _client.ReactionAdded -= HandleReaction;
            _cts.Cancel();
            _ = DeleteMessageInternalAsync();
            _ = OnEnded?.Invoke(_guild.Id);
        }

        return Task.CompletedTask;
    }

    private async Task DeleteMessageInternalAsync()
    {
        try
        {
            await msg.DeleteAsync();
        }
        catch
        {
            // the message may already be deleted
        }
    }

    private Task HandleReaction(
        Cacheable<IUserMessage, ulong> message,
        Cacheable<IMessageChannel, ulong> cacheable,
        SocketReaction r)
    {
        if (Stopped
            || message.Id != msg.Id
            || emote.Name != r.Emote.Name
            || (r.User.IsSpecified ? r.User.Value : null) is not IGuildUser gu // no unknown users, as they could be bots, or alts
            || gu.IsBot
            || (DateTime.UtcNow - gu.CreatedAt).TotalDays <= MIN_ACCOUNT_AGE_DAYS)
            return Task.CompletedTask;

        _payout.TryClaim(r.UserId);
        return Task.CompletedTask;
    }
}
