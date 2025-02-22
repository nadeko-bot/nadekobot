using Microsoft.Extensions.Caching.Memory;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Modules.Games.Services;
using System.Diagnostics.CodeAnalysis;

namespace NadekoBot.Modules.Games.Hangman;

public sealed class HangmanService : IHangmanService, IExecNoCommand
{
    private readonly ConcurrentDictionary<ulong, HangmanGame> _hangmanGames = new();
    private readonly IHangmanSource _source;
    private readonly IMessageSenderService _sender;
    private readonly GamesConfigService _gcs;
    private readonly ICurrencyService _cs;
    private readonly IMemoryCache _cdCache;
    private readonly object _locker = new();

    public HangmanService(
        IHangmanSource source,
        IMessageSenderService sender,
        GamesConfigService gcs,
        ICurrencyService cs,
        IMemoryCache cdCache)
    {
        _source = source;
        _sender = sender;
        _gcs = gcs;
        _cs = cs;
        _cdCache = cdCache;
    }

    public bool StartHangman(ulong channelId, string? category, [NotNullWhen(true)] out HangmanGame.State? state)
    {
        state = null;
        if (!_source.GetTerm(category, out var term))
            return false;


        var game = new HangmanGame(term);
        lock (_locker)
        {
            var hc = _hangmanGames.GetOrAdd(channelId, game);
            if (hc == game)
            {
                state = hc.GetState();
                return true;
            }

            return false;
        }
    }

    public ValueTask<bool> StopHangman(ulong channelId)
    {
        lock (_locker)
        {
            if (_hangmanGames.TryRemove(channelId, out _))
                return new(true);
        }

        return new(false);
    }

    public IReadOnlyCollection<string> GetHangmanTypes()
        => _source.GetCategories();

    public async Task ExecOnNoCommandAsync(IGuild guild, IUserMessage msg)
    {
        if (_hangmanGames.ContainsKey(msg.Channel.Id))
        {
            if (string.IsNullOrWhiteSpace(msg.Content))
                return;

            if (_cdCache.TryGetValue(msg.Author.Id, out _))
                return;

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
                    _cdCache.Set(msg.Author.Id,
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

            await SendState((ITextChannel)msg.Channel, msg.Author, msg.Content, state);
        }
    }

    private Task<IUserMessage> SendState(
        ITextChannel channel,
        IUser user,
        string content,
        HangmanGame.State state)
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

        return _sender.Response(channel).Embed(embed).SendAsync();
    }
}