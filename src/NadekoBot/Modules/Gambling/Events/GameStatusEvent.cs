#nullable disable
using NadekoBot.Db.Models;
using System.Collections.Concurrent;

namespace NadekoBot.Modules.Gambling.Common.Events;

public class GameStatusEvent : ICurrencyEvent
{
    public event Func<ulong, Task> OnEnded;
    private long PotSize { get; set; }
    public bool Stopped { get; private set; }
    public bool PotEmptied { get; private set; }
    private readonly DiscordSocketClient _client;
    private readonly IGuild _guild;
    private IUserMessage msg;
    private readonly ICurrencyService _cs;
    private readonly long _amount;

    private readonly Func<CurrencyEvent.Type, EventOptions, long, EmbedBuilder> _embedFunc;
    private readonly bool _isPotLimited;
    private readonly ITextChannel _channel;
    private readonly ConcurrentHashSet<ulong> _awardedUsers = new();
    private readonly ConcurrentQueue<ulong> _toAward = new();
    private readonly Timer _t;
    private readonly Timer _timeout;
    private readonly EventOptions _opts;

    private readonly string _code;

    private readonly object _stopLock = new();

    private readonly object _potLock = new();
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
        _cs = cs;
        _amount = opt.Amount;
        PotSize = opt.PotSize;
        _embedFunc = embedFunc;
        _isPotLimited = PotSize > 0;
        _channel = ch;
        _opts = opt;
        _sender = sender;
        // generate code

        _code = new kwum(_rng.Next(1_000_000, 10_000_000)).ToString();

        _t = new(OnTimerTick, null, Timeout.InfiniteTimeSpan, TimeSpan.FromSeconds(2));
        if (_opts.Hours > 0)
            _timeout = new(EventTimeout, null, TimeSpan.FromHours(_opts.Hours), Timeout.InfiniteTimeSpan);
    }

    private void EventTimeout(object state)
        => _ = StopEvent();

    private async void OnTimerTick(object state)
    {
        var potEmpty = PotEmptied;
        var toAward = new List<ulong>();
        while (_toAward.TryDequeue(out var x))
            toAward.Add(x);

        if (!toAward.Any())
            return;

        try
        {
            await _cs.AddBulkAsync(toAward,
                _amount,
                new("event", "gamestatus")
            );

            if (_isPotLimited)
            {
                await msg.ModifyAsync(m =>
                {
                    m.Embed = GetEmbed(PotSize).Build();
                });
            }

            Log.Information("Game status event awarded {Count} users {Amount} currency.{Remaining}",
                toAward.Count,
                _amount,
                _isPotLimited ? $" {PotSize} left." : "");

            if (potEmpty)
                _ = StopEvent();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error in OnTimerTick in gamestatusevent");
        }
    }

    public async Task StartEvent()
    {
        msg = await _sender.Response(_channel).Embed(GetEmbed(_opts.PotSize)).SendAsync();
        await _client.SetGameAsync(_code);
        _client.MessageDeleted += OnMessageDeleted;
        _client.MessageReceived += HandleMessage;
        _t.Change(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    private EmbedBuilder GetEmbed(long pot)
        => _embedFunc(CurrencyEvent.Type.GameStatus, _opts, pot);

    private async Task OnMessageDeleted(Cacheable<IMessage, ulong> message, Cacheable<IMessageChannel, ulong> cacheable)
    {
        if (message.Id == msg.Id)
            await StopEvent();
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
            _t.Change(Timeout.Infinite, Timeout.Infinite);
            _timeout?.Change(Timeout.Infinite, Timeout.Infinite);
            _ = _client.SetGameAsync(null);
            try
            {
                _ = msg.DeleteAsync();
            }
            catch { }

            _ = OnEnded?.Invoke(_guild.Id);
        }

        return Task.CompletedTask;
    }

    private Task HandleMessage(SocketMessage message)
    {
        _ = Task.Run(async () =>
        {
            if (message.Author is not IGuildUser gu // no unknown users, as they could be bots, or alts
                || gu.IsBot // no bots
                || message.Content != _code // code has to be the same
                || (DateTime.UtcNow - gu.CreatedAt).TotalDays <= 5) // no recently created accounts
                return;
            // there has to be money left in the pot
            // and the user wasn't rewarded
            if (_awardedUsers.Add(message.Author.Id) && TryTakeFromPot())
            {
                _toAward.Enqueue(message.Author.Id);
                if (_isPotLimited && PotSize < _amount)
                    PotEmptied = true;
            }

            try
            {
                await message.DeleteAsync(new()
                {
                    RetryMode = RetryMode.AlwaysFail
                });
            }
            catch { }
        });
        return Task.CompletedTask;
    }

    private bool TryTakeFromPot()
    {
        if (_isPotLimited)
        {
            lock (_potLock)
            {
                if (PotSize < _amount)
                    return false;

                PotSize -= _amount;
                return true;
            }
        }

        return true;
    }
}