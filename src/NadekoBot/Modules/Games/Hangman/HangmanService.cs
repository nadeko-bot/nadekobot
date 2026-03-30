using Microsoft.Extensions.Caching.Memory;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Modules.Games.Services;
using System.Diagnostics.CodeAnalysis;
using NadekoBot.Modules.Games.Quests;

namespace NadekoBot.Modules.Games.Hangman;

public sealed class HangmanService : IHangmanService, IExecNoCommand
{
    private const int REPOST_THRESHOLD = 5;

    private readonly ConcurrentDictionary<ulong, HangmanGame> _hangmanGames = new();
    private readonly ConcurrentDictionary<ulong, HangmanMessageState> _messageStates = new();
    private readonly IHangmanSource _source;
    private readonly IMessageSenderService _sender;
    private readonly GamesConfigService _gcs;
    private readonly ICurrencyService _cs;
    private readonly IMemoryCache _cdCache;
    private readonly QuestService _quests;
    private readonly Lock _locker = new();

    public HangmanService(
        IHangmanSource source,
        IMessageSenderService sender,
        GamesConfigService gcs,
        ICurrencyService cs,
        IMemoryCache cdCache,
        QuestService quests)
    {
        _source = source;
        _sender = sender;
        _gcs = gcs;
        _cs = cs;
        _cdCache = cdCache;
        _quests = quests;
    }

    public bool StartHangman(ulong channelId, string? category, [NotNullWhen(true)] out HangmanGame.State? state)
    {
        state = null;
        if (!_source.GetTerm(category, out var termData))
            return false;

        var game = new HangmanGame(termData.Value.Term, termData.Value.Category);
        lock (_locker)
        {
            var hc = _hangmanGames.GetOrAdd(channelId, game);
            if (hc == game)
            {
                _messageStates[channelId] = new();
                state = hc.GetState();
                return true;
            }

            return false;
        }
    }

    public void SetLastMessage(ulong channelId, IUserMessage msg)
    {
        if (_messageStates.TryGetValue(channelId, out var ms))
            ms.SetMessage(msg);
    }

    public ValueTask<bool> StopHangman(ulong channelId)
    {
        lock (_locker)
        {
            if (_hangmanGames.TryRemove(channelId, out _))
            {
                _messageStates.TryRemove(channelId, out _);
                return new(true);
            }
        }

        return new(false);
    }

    public IReadOnlyCollection<string> GetHangmanTypes()
        => _source.GetCategories();

    public async Task ExecOnNoCommandAsync(IGuild guild, IUserMessage msg)
    {
        if (!_hangmanGames.ContainsKey(msg.Channel.Id))
            return;

        if (string.IsNullOrWhiteSpace(msg.Content))
            return;

        if (_cdCache.TryGetValue("hangman:" + msg.Author.Id, out _))
            return;

        // Every channel message increments the counter.
        // When the threshold is reached, repost the current board so it doesn't drift offscreen.
        if (_messageStates.TryGetValue(msg.Channel.Id, out var msgState))
        {
            msgState.IncrementCounter();
            if (msgState.Counter >= REPOST_THRESHOLD)
            {
                msgState.ResetCounter();

                HangmanGame.State? currentState = null;
                lock (_locker)
                {
                    if (_hangmanGames.TryGetValue(msg.Channel.Id, out var g))
                        currentState = g.GetState();
                }

                if (currentState is not null)
                {
                    var embed = Games.HangmanCommands.GetEmbed(_sender, currentState);
                    var sent = await _sender.Response((ITextChannel)msg.Channel).Embed(embed).SendAsync();
                    msgState.SetMessage(sent);
                }
            }
        }

        HangmanGame.State state;
        long rew = 0;
        lock (_locker)
        {
            if (!_hangmanGames.TryGetValue(msg.Channel.Id, out var game))
                return;

            state = game.Guess(msg.Content.ToLowerInvariant());

            if (state.GuessResult == HangmanGame.GuessResult.NoAction)
                return;

            if (state.GuessResult is HangmanGame.GuessResult.Incorrect or HangmanGame.GuessResult.AlreadyTried)
            {
                _cdCache.Set("hangman:" + msg.Author.Id,
                    string.Empty,
                    new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(3)
                    });
            }

            if (state.Phase == HangmanGame.Phase.Ended)
            {
                if (_hangmanGames.TryRemove(msg.Channel.Id, out _))
                    rew = _gcs.Data.Hangman.CurrencyReward;
            }
        }

        if (rew > 0)
            await _cs.AddAsync(msg.Author, rew, new("hangman", "win"));

        if (state.GuessResult == HangmanGame.GuessResult.Win)
            await _quests.ReportActionAsync(msg.Author.Id, QuestEventType.GameWon, new() { { "game", "hangman" } });

        await SendOrEditState((ITextChannel)msg.Channel, msg.Author, msg.Content, state);

        if (state.Phase == HangmanGame.Phase.Ended)
            _messageStates.TryRemove(msg.Channel.Id, out _);
    }

    private async Task SendOrEditState(
        ITextChannel channel,
        IUser user,
        string content,
        HangmanGame.State state)
    {
        var embed = BuildEmbed(user, content, state);

        if (_messageStates.TryGetValue(channel.Id, out var msgState))
        {
            var lastMsg = msgState.LastMessage;
            if (lastMsg is not null)
            {
                try
                {
                    await lastMsg.ModifyAsync(m =>
                    {
                        m.Embed = embed.Build();
                        m.Content = "";
                    });
                    return;
                }
                catch
                {
                    // message was deleted or can't be edited, fall through to send new
                }
            }
        }

        var sent = await _sender.Response(channel).Embed(embed).SendAsync();

        if (_messageStates.TryGetValue(channel.Id, out msgState))
            msgState.SetMessage(sent);
    }

    private EmbedBuilder BuildEmbed(IUser user, string content, HangmanGame.State state)
    {
        var embed = Games.HangmanCommands.GetEmbed(_sender, state);

        if (state.GuessResult == HangmanGame.GuessResult.Guess)
            embed.WithDescription($"{user} guessed the letter {content}!").WithOkColor();
        else if (state.GuessResult == HangmanGame.GuessResult.Incorrect && state.Failed)
            embed.WithDescription($"{user} Letter {content} doesn't exist! Game over!").WithErrorColor();
        else if (state.GuessResult == HangmanGame.GuessResult.Incorrect)
            embed.WithDescription($"{user} Letter {content} doesn't exist!").WithErrorColor();
        else if (state.GuessResult == HangmanGame.GuessResult.AlreadyTried)
            embed.WithDescription($"{user} Letter {content} has already been used.").WithPendingColor();
        else if (state.GuessResult == HangmanGame.GuessResult.Win)
            embed.WithDescription($"{user} won!").WithOkColor();

        if (!string.IsNullOrWhiteSpace(state.ImageUrl) && Uri.IsWellFormedUriString(state.ImageUrl, UriKind.Absolute))
            embed.WithImageUrl(state.ImageUrl);

        return embed;
    }
}

/// <summary>
/// Tracks the bot's last hangman message and the count of messages since it was posted.
/// </summary>
internal sealed class HangmanMessageState
{
    private IUserMessage? _lastMessage;
    private int _counter;

    public IUserMessage? LastMessage => _lastMessage;
    public int Counter => _counter;

    public void SetMessage(IUserMessage msg)
        => _lastMessage = msg;

    public void IncrementCounter()
        => Interlocked.Increment(ref _counter);

    public void ResetCounter()
        => Interlocked.Exchange(ref _counter, 0);
}
