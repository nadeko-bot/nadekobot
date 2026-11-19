using System.Net;
using Microsoft.Extensions.Caching.Memory;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Modules.Games.Quests;
using NadekoBot.Modules.Games.SavedGames;
using NadekoBot.Modules.Games.Services;

namespace NadekoBot.Modules.Games.Hangman;

public sealed class HangmanService(
    IHangmanSource source,
    IMessageSenderService sender,
    GamesConfigService gcs,
    IMemoryCache cdCache,
    QuestService quests,
    IBotStrings strings,
    SavedGameStore store,
    DiscordSocketClient client,
    IntentState intents) : IHangmanService, IExecNoCommand, ISavedGameHandler
{
    public const int MAX_GAMES_PER_SERVER = 5;
    private const int REPOST_THRESHOLD = 5;

    private readonly ConcurrentDictionary<ulong, HangmanGame> _hangmanGames = new();
    private readonly ConcurrentDictionary<ulong, HangmanMessageState> _messageStates = new();
    private readonly Lock _locker = new();

    public SavedGameKind Kind
        => SavedGameKind.Hangman;

    public async Task<(HangmanStartResult Result, HangmanGame.State? State)> StartHangmanAsync(
        ulong guildId,
        ulong channelId,
        string? category)
    {
        if (_hangmanGames.ContainsKey(channelId))
            return (HangmanStartResult.AlreadyRunning, null);

        if (!source.GetTerm(category, out var termData))
            return (HangmanStartResult.NoTerm, null);

        var game = new HangmanGame(termData.Value.Term, termData.Value.Category) { GuildId = guildId };
        var state = game.Save();
        var add = await store.AddAsync(guildId, channelId, Kind, MAX_GAMES_PER_SERVER, state);

        // a game in a deleted channel cannot be stopped, so it must not hold a place of the limit
        if (add.Result == SavedGameAddResult.LimitReached && await EndGamesOfDeletedChannelsInternalAsync(guildId) > 0)
            add = await store.AddAsync(guildId, channelId, Kind, MAX_GAMES_PER_SERVER, state);

        if (add.Result != SavedGameAddResult.Added)
        {
            return (add.Result == SavedGameAddResult.LimitReached
                ? HangmanStartResult.LimitReached
                : HangmanStartResult.AlreadyRunning, null);
        }

        game.SavedGameId = add.Id;
        lock (_locker)
        {
            if (_hangmanGames.TryAdd(channelId, game))
            {
                _messageStates[channelId] = new();
                return (HangmanStartResult.Started, game.GetState());
            }
        }

        await store.EndOrRetryAsync(add.Id);
        return (HangmanStartResult.AlreadyRunning, null);
    }

    private async Task<int> EndGamesOfDeletedChannelsInternalAsync(ulong guildId)
    {
        if (client.GetGuild(guildId) is not { } guild)
            return 0;

        List<HangmanGame>? gone = null;
        lock (_locker)
        {
            foreach (var (channelId, game) in _hangmanGames)
            {
                if (game.GuildId != guildId || guild.GetChannel(channelId) is not null)
                    continue;

                if (_hangmanGames.TryRemove(channelId, out _))
                {
                    _messageStates.TryRemove(channelId, out _);
                    (gone ??= new()).Add(game);
                }
            }
        }

        if (gone is null)
            return 0;

        foreach (var game in gone)
            await store.EndOrRetryAsync(game.SavedGameId);

        return gone.Count;
    }

    public void SetLastMessage(ulong channelId, IUserMessage msg)
    {
        if (_messageStates.TryGetValue(channelId, out var ms))
            ms.SetMessage(msg);
    }

    public async ValueTask<bool> StopHangman(ulong channelId)
    {
        HangmanGame? game;
        lock (_locker)
        {
            if (!_hangmanGames.TryRemove(channelId, out game))
                return false;

            _messageStates.TryRemove(channelId, out _);
        }

        await store.EndOrRetryAsync(game.SavedGameId);
        return true;
    }

    public IReadOnlyCollection<string> GetHangmanTypes()
        => source.GetCategories();

    public async ValueTask ExecOnNoCommandAsync(IGuild? guild, IUserMessage msg)
    {
        if (!_hangmanGames.ContainsKey(msg.Channel.Id))
            return;

        if (string.IsNullOrWhiteSpace(msg.Content))
            return;

        if (cdCache.TryGetValue("hangman:" + msg.Author.Id, out _))
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
                    var embed = Games.HangmanCommands.GetEmbed(sender, currentState);
                    var sent = await sender.Response((ITextChannel)msg.Channel).Embed(embed).SendAsync();
                    msgState.SetMessage(sent);
                }
            }
        }

        HangmanGame.State state;
        HangmanGame? ended = null;
        lock (_locker)
        {
            if (!_hangmanGames.TryGetValue(msg.Channel.Id, out var game))
                return;

            state = game.Guess(msg.Content);

            if (state.GuessResult == HangmanGame.GuessResult.NoAction)
                return;

            if (state.GuessResult is HangmanGame.GuessResult.Incorrect or HangmanGame.GuessResult.AlreadyTried)
            {
                cdCache.Set("hangman:" + msg.Author.Id,
                    string.Empty,
                    new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(3)
                    });
            }

            if (state.Phase == HangmanGame.Phase.Ended)
            {
                if (_hangmanGames.TryRemove(msg.Channel.Id, out _))
                    ended = game;
            }
            else if (state.GuessResult is HangmanGame.GuessResult.Guess or HangmanGame.GuessResult.Incorrect)
            {
                // under the lock, so two guesses cannot save their states in the wrong order
                store.Update(game.SavedGameId, game.Save());
            }
        }

        var won = state.GuessResult == HangmanGame.GuessResult.Win;
        if (ended is not null)
        {
            var reward = won ? gcs.Data.Hangman.CurrencyReward : 0;
            await store.EndOrRetryAsync(ended.SavedGameId, msg.Author.Id, reward, new("hangman", "win"));
        }

        if (won)
            await quests.ReportActionAsync(msg.Author.Id, QuestEventType.GameWon, new() { { "game", "hangman" } });

        await SendOrEditState((ITextChannel)msg.Channel, msg.Author, msg.Content, state);

        if (state.Phase == HangmanGame.Phase.Ended)
            _messageStates.TryRemove(msg.Channel.Id, out _);
    }

    public async Task<SavedGameRestoreResult> RestoreAsync(SavedGame saved)
    {
        if (!intents.MessageContent)
            return SavedGameRestoreResult.Drop;

        if (HangmanGame.Load(saved.State) is not { } game)
            return SavedGameRestoreResult.Drop;

        if (SavedGameChannel.Find(client, saved, out var retry) is not { } channel)
            return retry ? SavedGameRestoreResult.Retry : SavedGameRestoreResult.Drop;

        game.SavedGameId = saved.Id;
        game.GuildId = saved.GuildId;

        IUserMessage sent;
        try
        {
            var embed = Games.HangmanCommands.GetEmbed(sender, game.GetState())
                .WithDescription(strings.GetText(strs.game_restored, saved.GuildId));

            sent = await sender.Response(channel).Embed(embed).SendAsync();
        }
        catch (HttpException ex) when (ex.HttpCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            Log.Warning(ex, "Hangman could not post the game in channel {ChannelId} after a restart", saved.ChannelId);
            return SavedGameRestoreResult.Drop;
        }

        lock (_locker)
        {
            if (!_hangmanGames.TryAdd(saved.ChannelId, game))
                return SavedGameRestoreResult.Drop;

            var state = new HangmanMessageState();
            state.SetMessage(sent);
            _messageStates[saved.ChannelId] = state;
        }

        return SavedGameRestoreResult.Restored;
    }

    public Task DropAsync(SavedGame saved)
        => store.EndAsync(saved.Id);

    private async Task SendOrEditState(
        ITextChannel channel,
        IUser user,
        string content,
        HangmanGame.State state)
    {
        var embed = BuildEmbed(channel.GuildId, user, content, state);

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

        var sent = await sender.Response(channel).Embed(embed).SendAsync();

        if (_messageStates.TryGetValue(channel.Id, out msgState))
            msgState.SetMessage(sent);
    }

    private EmbedBuilder BuildEmbed(ulong guildId, IUser user, string content, HangmanGame.State state)
    {
        var embed = Games.HangmanCommands.GetEmbed(sender, state);
        var name = user.ToString();

        if (state.GuessResult == HangmanGame.GuessResult.Guess)
            embed.WithDescription(strings.GetText(strs.hangman_guessed(name, content), guildId)).WithOkColor();
        else if (state.GuessResult == HangmanGame.GuessResult.Incorrect && state.Failed)
            embed.WithDescription(strings.GetText(strs.hangman_letter_missing_lost(name, content), guildId)).WithErrorColor();
        else if (state.GuessResult == HangmanGame.GuessResult.Incorrect)
            embed.WithDescription(strings.GetText(strs.hangman_letter_missing(name, content), guildId)).WithErrorColor();
        else if (state.GuessResult == HangmanGame.GuessResult.AlreadyTried)
            embed.WithDescription(strings.GetText(strs.hangman_letter_used(name, content), guildId)).WithPendingColor();
        else if (state.GuessResult == HangmanGame.GuessResult.Win)
            embed.WithDescription(strings.GetText(strs.hangman_won(name), guildId)).WithOkColor();

        if (!string.IsNullOrWhiteSpace(state.ImageUrl) && Uri.IsWellFormedUriString(state.ImageUrl, UriKind.Absolute))
            embed.WithImageUrl(state.ImageUrl);

        return embed;
    }
}

public sealed class HangmanMessageState
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
