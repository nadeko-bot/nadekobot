#nullable disable
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Gambling.Common.Events;

public class GameStatusEvent : ICurrencyEvent
{
    private const int MIN_ACCOUNT_AGE_DAYS = 5;

    public event Func<ulong, Task> OnEnded;
    public bool Stopped { get; private set; }
    private readonly DiscordSocketClient _client;
    private readonly IGuild _guild;
    private IUserMessage msg;

    private readonly Func<CurrencyEvent.Type, EventOptions, long, EmbedBuilder> _embedFunc;
    private readonly ITextChannel _channel;
    private readonly EventPayout _payout;
    private readonly CancellationTokenSource _cts = new();
    private readonly EventOptions _opts;

    private readonly string _code;

    private readonly Lock _stopLock = new();
    private readonly IMessageSenderService _sender;

    private static readonly NadekoRandom _rng = new NadekoRandom();

    public GameStatusEvent(
        DiscordSocketClient client,
        ICurrencyService cs,
        SocketGuild g,
        ITextChannel ch,
        EventOptions opt,
        IMessageSenderService sender,
        Func<CurrencyEvent.Type, EventOptions, long, EmbedBuilder> embedFunc)
    {
        _client = client;
        _guild = g;
        _embedFunc = embedFunc;
        _channel = ch;
        _opts = opt;
        _sender = sender;
        _payout = new(cs, opt.Amount, opt.PotSize, new("event", "gamestatus"));

        _code = new kwum(_rng.Next(1_000_000, 10_000_000)).ToString();
    }

    public async Task StartEvent()
    {
        msg = await _sender.Response(_channel).Embed(GetEmbed(_opts.PotSize)).SendAsync();
        await _client.SetGameAsync(_code);
        _client.MessageDeleted += OnMessageDeleted;
        _client.MessageReceived += HandleMessage;
        _ = Task.Run(() => _payout.RunAsync(TimeSpan.FromHours(_opts.Hours),
            pot => msg.ModifyAsync(m => m.Embed = GetEmbed(pot).Build()),
            StopEvent,
            _cts.Token));
    }

    private EmbedBuilder GetEmbed(long pot)
        => _embedFunc(CurrencyEvent.Type.GameStatus, _opts, pot);

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
            _client.MessageReceived -= HandleMessage;
            _cts.Cancel();
            _ = _client.SetGameAsync(null);
            _ = DeleteInternalAsync(msg);
            _ = OnEnded?.Invoke(_guild.Id);
        }

        return Task.CompletedTask;
    }

    private static async Task DeleteInternalAsync(IMessage message)
    {
        try
        {
            await message.DeleteAsync(new()
            {
                RetryMode = RetryMode.AlwaysFail
            });
        }
        catch
        {
            // the message may already be deleted or the bot may lack permissions
        }
    }

    private Task HandleMessage(SocketMessage message)
    {
        if (Stopped
            || message.Content != _code
            || message.Author is not IGuildUser gu // no unknown users, as they could be bots, or alts
            || gu.IsBot
            || (DateTime.UtcNow - gu.CreatedAt).TotalDays <= MIN_ACCOUNT_AGE_DAYS)
            return Task.CompletedTask;

        _payout.TryClaim(message.Author.Id);
        _ = DeleteInternalAsync(message);
        return Task.CompletedTask;
    }
}
