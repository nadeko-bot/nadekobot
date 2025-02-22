#nullable disable
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Gambling.Common.Events;

public class ReactionEvent : ICurrencyEvent
{
    public event Func<ulong, Task> OnEnded;
    private long PotSize { get; set; }
    public bool Stopped { get; private set; }
    public bool PotEmptied { get; private set; }
    private readonly DiscordSocketClient _client;
    private readonly IGuild _guild;
    private IUserMessage msg;
    private IEmote emote;
    private readonly ICurrencyService _cs;
    private readonly long _amount;

    private readonly Func<CurrencyEvent.Type, EventOptions, long, EmbedBuilder> _embedFunc;
    private readonly bool _isPotLimited;
    private readonly ITextChannel _channel;
    private readonly ConcurrentHashSet<ulong> _awardedUsers = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<ulong> _toAward = new();
    private readonly Timer _t;
    private readonly Timer _timeout;
    private readonly bool _noRecentlyJoinedServer;
    private readonly EventOptions _opts;
    private readonly GamblingConfig _config;

    private readonly object _stopLock = new();

    private readonly object _potLock = new();
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
        _cs = cs;
        _amount = opt.Amount;
        PotSize = opt.PotSize;
        _embedFunc = embedFunc;
        _isPotLimited = PotSize > 0;
        _channel = ch;
        _noRecentlyJoinedServer = false;
        _opts = opt;
        _config = config;
        _sender = sender;

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
            await _cs.AddBulkAsync(toAward, _amount, new("event", "reaction"));

            if (_isPotLimited)
            {
                await msg.ModifyAsync(m =>
                    {
                        m.Embed = GetEmbed(PotSize).Build();
                    });
            }

            Log.Information("Reaction Event awarded {Count} users {Amount} currency.{Remaining}",
                toAward.Count,
                _amount,
                _isPotLimited ? $" {PotSize} left." : "");

            if (potEmpty)
                _ = StopEvent();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error adding bulk currency to users");
        }
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
        _t.Change(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    private EmbedBuilder GetEmbed(long pot)
        => _embedFunc(CurrencyEvent.Type.Reaction, _opts, pot);

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
            _client.ReactionAdded -= HandleReaction;
            _t.Change(Timeout.Infinite, Timeout.Infinite);
            _timeout?.Change(Timeout.Infinite, Timeout.Infinite);
            try
            {
                _ = msg.DeleteAsync();
            }
            catch { }

            _ = OnEnded?.Invoke(_guild.Id);
        }

        return Task.CompletedTask;
    }

    private Task HandleReaction(
        Cacheable<IUserMessage, ulong> message,
        Cacheable<IMessageChannel, ulong> cacheable,
        SocketReaction r)
    {
        _ = Task.Run(() =>
        {
            if (emote.Name != r.Emote.Name)
                return;
            if ((r.User.IsSpecified
                    ? r.User.Value
                    : null) is not IGuildUser gu // no unknown users, as they could be bots, or alts
                || message.Id != msg.Id // same message
                || gu.IsBot // no bots
                || (DateTime.UtcNow - gu.CreatedAt).TotalDays <= 5 // no recently created accounts
                || (_noRecentlyJoinedServer
                    && // if specified, no users who joined the server in the last 24h
                    (gu.JoinedAt is null
                     || (DateTime.UtcNow - gu.JoinedAt.Value).TotalDays
                     < 1))) // and no users for who we don't know when they joined
                return;
            // there has to be money left in the pot
            // and the user wasn't rewarded
            if (_awardedUsers.Add(r.UserId) && TryTakeFromPot())
            {
                _toAward.Enqueue(r.UserId);
                if (_isPotLimited && PotSize < _amount)
                    PotEmptied = true;
            }
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