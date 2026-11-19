using System.Net;
using System.Text;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Modules.Games.Common.Trivia;
using NadekoBot.Modules.Games.SavedGames;
using NadekoBot.Modules.Games.Services;

namespace NadekoBot.Modules.Games;

public enum TriviaStopResult : byte
{
    NotRunning,
    Stopping,
    AlreadyStopping
}

// Owns the games and their messages, so a game which a restart interrupted continues without a command.
public sealed class TriviaGamesService(
    DiscordSocketClient client,
    ILocalDataCache cache,
    IMessageSenderService sender,
    IBotStrings strings,
    ICommandHandler cmdHandler,
    GamesConfigService gamesConfig,
    SavedGameStore store,
    IntentState intents) : IReadyExecutor, INService, ISavedGameHandler
{
    public const int MAX_GAMES_PER_SERVER = 1;

    private readonly ConcurrentDictionary<ulong, TriviaRun> _runs = new();
    private int _subscribed;

    public SavedGameKind Kind
        => SavedGameKind.Trivia;

    public Task OnReadyAsync()
    {
        if (Interlocked.Exchange(ref _subscribed, 1) == 0)
            client.MessageReceived += OnMessageReceivedInternal;

        return Task.CompletedTask;
    }

    private Task OnMessageReceivedInternal(SocketMessage msg)
    {
        if (msg.Author.IsBot || msg is not SocketUserMessage { Channel: IGuildChannel gc } umsg)
            return Task.CompletedTask;

        if (_runs.TryGetValue(gc.GuildId, out var run) && run.Channel.Id == gc.Id)
            run.Game.TryInput(new(umsg.Author.Mention, umsg.Author.Id), umsg.Content);

        return Task.CompletedTask;
    }

    // false when the server already has a game
    public async Task<bool> StartAsync(ITextChannel channel, TriviaOptions options)
    {
        if (_runs.ContainsKey(channel.GuildId))
            return false;

        var game = new TriviaGame(options, cache, channel.Id);
        var add = await store.AddAsync(channel.GuildId,
            channel.Id,
            SavedGameKind.Trivia,
            MAX_GAMES_PER_SERVER,
            game.Save());

        if (add.Result != SavedGameAddResult.Added)
            return false;

        if (TryRunInternal(new(game, channel, add.Id)))
            return true;

        await store.EndOrRetryAsync(add.Id);
        return false;
    }

    public async Task RepostQuestionAsync(ulong guildId)
    {
        if (_runs.TryGetValue(guildId, out var run))
            await run.Game.TriggerQuestionAsync();
    }

    public async Task<bool> ShowLeaderboardAsync(ulong guildId)
    {
        if (!_runs.TryGetValue(guildId, out var run))
            return false;

        await run.Game.TriggerStatsAsync();
        return true;
    }

    public async Task<TriviaStopResult> StopAsync(ulong guildId)
    {
        if (!_runs.TryGetValue(guildId, out var run))
            return TriviaStopResult.NotRunning;

        if (!run.Game.Stop())
            return TriviaStopResult.AlreadyStopping;

        // a stopped game must not come back after a restart, so the stop does not wait for the batch
        try
        {
            await store.SaveNowAsync(run.SavedId, run.Game.Save());
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Trivia could not save the stop of the game in channel {ChannelId}. The next batch saves it", run.Channel.Id);
        }

        return TriviaStopResult.Stopping;
    }

    public async Task<SavedGameRestoreResult> RestoreAsync(SavedGame saved)
    {
        if (!intents.MessageContent)
            return SavedGameRestoreResult.Drop;

        if (TriviaGame.Load(saved.State, cache, saved.ChannelId) is not { } game)
            return SavedGameRestoreResult.Drop;

        if (SavedGameChannel.Find(client, saved, out var retry) is not { } channel)
            return retry ? SavedGameRestoreResult.Retry : SavedGameRestoreResult.Drop;

        try
        {
            await sender.Response(channel)
                .Confirm(strings.GetText(strs.trivia_game, saved.GuildId),
                    strings.GetText(strs.game_restored, saved.GuildId))
                .SendAsync();
        }
        catch (HttpException ex) when (ex.HttpCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            Log.Warning(ex, "Trivia could not post in channel {ChannelId} after a restart", saved.ChannelId);
            return SavedGameRestoreResult.Drop;
        }

        return TryRunInternal(new(game, channel, saved.Id))
            ? SavedGameRestoreResult.Restored
            : SavedGameRestoreResult.Drop;
    }

    public Task DropAsync(SavedGame saved)
        => store.EndAsync(saved.Id);

    private bool TryRunInternal(TriviaRun run)
    {
        if (!_runs.TryAdd(run.Channel.GuildId, run))
            return false;

        var game = run.Game;
        game.OnQuestion += (_, question) => OnQuestionInternalAsync(run, question);
        game.OnHint += (_, question) => OnHintInternalAsync(run, question);
        game.OnGuess += (_, user, question, isWin) => OnGuessInternalAsync(run, user, question, isWin);
        game.OnTimeout += (_, question) => OnTimeoutInternalAsync(run, question);
        game.OnStats += _ => SendLeaderboardInternalAsync(run, false);
        game.OnEnded += _ => OnEndedInternalAsync(run);

        _ = Task.Run(game.RunAsync);
        return true;
    }

    private string GetTextInternal(TriviaRun run, LocStr str)
        => strings.GetText(str, run.Channel.GuildId);

    private async Task OnQuestionInternalAsync(TriviaRun run, TriviaQuestion question)
    {
        try
        {
            var embed = sender.CreateEmbed(run.Channel.GuildId)
                .WithOkColor()
                .WithTitle(GetTextInternal(run, strs.trivia_game))
                .AddField(GetTextInternal(run, strs.category), question.Category)
                .AddField(GetTextInternal(run, strs.question), question.Question);

            run.ShowHowToQuit = !run.ShowHowToQuit;
            if (run.ShowHowToQuit)
                embed.WithFooter(GetTextInternal(run, strs.trivia_quit(cmdHandler.GetPrefix(run.Channel.GuildId) + "tq")));

            if (Uri.IsWellFormedUriString(question.ImageUrl, UriKind.Absolute))
                embed.WithImageUrl(question.ImageUrl);

            run.QuestionEmbed = embed;
            run.QuestionMessage = await sender.Response(run.Channel).Embed(embed).SendAsync();
        }
        catch (HttpException ex) when (ex.HttpCode is HttpStatusCode.NotFound
                                           or HttpStatusCode.Forbidden
                                           or HttpStatusCode.BadRequest)
        {
            Log.Warning("Unable to send trivia questions. Stopping immediately");
            run.Game.Stop();
            throw;
        }

        store.Update(run.SavedId, run.Game.Save());
    }

    private async Task OnHintInternalAsync(TriviaRun run, TriviaQuestion question)
    {
        try
        {
            if (run.QuestionMessage is not { } msg)
            {
                run.Game.Stop();
                return;
            }

            if (run.QuestionEmbed is { } embed)
                await msg.ModifyAsync(m => m.Embed = embed.WithFooter(question.GetHint()).Build());
        }
        catch (HttpException ex) when (ex.HttpCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            Log.Warning("Unable to edit message to show hint. Stopping trivia");
            run.Game.Stop();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error editing trivia message");
        }
    }

    private async Task OnGuessInternalAsync(TriviaRun run, TriviaUser user, TriviaQuestion question, bool isWin)
    {
        if (!isWin)
            store.Update(run.SavedId, run.Game.Save());

        try
        {
            var text = isWin
                ? GetTextInternal(run, strs.trivia_win(user.Name, Format.Bold(question.Answer)))
                : GetTextInternal(run, strs.trivia_guess(user.Name, Format.Bold(question.Answer)));

            var embed = sender.CreateEmbed(run.Channel.GuildId)
                .WithOkColor()
                .WithTitle(GetTextInternal(run, strs.trivia_game))
                .WithDescription(text);

            if (Uri.IsWellFormedUriString(question.AnswerImageUrl, UriKind.Absolute))
                embed.WithImageUrl(question.AnswerImageUrl);

            await sender.Response(run.Channel).Embed(embed).SendAsync();
        }
        catch
        {
            // the game goes on without this message
        }
    }

    private async Task OnTimeoutInternalAsync(TriviaRun run, TriviaQuestion question)
    {
        store.Update(run.SavedId, run.Game.Save());

        try
        {
            var embed = sender.CreateEmbed(run.Channel.GuildId)
                .WithErrorColor()
                .WithTitle(GetTextInternal(run, strs.trivia_game))
                .WithDescription(GetTextInternal(run, strs.trivia_times_up(Format.Bold(question.Answer))));

            if (Uri.IsWellFormedUriString(question.AnswerImageUrl, UriKind.Absolute))
                embed.WithImageUrl(question.AnswerImageUrl);

            await sender.Response(run.Channel).Embed(embed).SendAsync();
        }
        catch
        {
            // the game goes on without this message
        }
    }

    private async Task SendLeaderboardInternalAsync(TriviaRun run, bool ended)
    {
        try
        {
            var board = new StringBuilder();
            foreach (var (id, pts) in run.Game.GetLeaderboard())
                board.AppendLine(GetTextInternal(run, strs.trivia_points(Format.Bold($"<@{id}>"), pts)));

            if (!ended)
            {
                await sender.Response(run.Channel)
                    .Confirm(GetTextInternal(run, strs.leaderboard), board.ToString())
                    .SendAsync();
                return;
            }

            await sender.Response(run.Channel)
                .Embed(sender.CreateEmbed(run.Channel.GuildId)
                    .WithOkColor()
                    .WithAuthor(GetTextInternal(run, strs.trivia_ended))
                    .WithTitle(GetTextInternal(run, strs.leaderboard))
                    .WithDescription(board.ToString()))
                .SendAsync();
        }
        catch
        {
            // the leaderboard is only information
        }
    }

    private async Task OnEndedInternalAsync(TriviaRun run)
    {
        try
        {
            var winner = run.Game.WinnerId;
            var reward = winner is null ? 0 : gamesConfig.Data.Trivia.CurrencyReward;
            await store.EndOrRetryAsync(run.SavedId, winner ?? 0, reward, new("trivia", "win"));
        }
        finally
        {
            if (_runs.TryGetValue(run.Channel.GuildId, out var current) && ReferenceEquals(current, run))
                _runs.TryRemove(run.Channel.GuildId, out _);
        }

        await SendLeaderboardInternalAsync(run, true);
    }

    private sealed class TriviaRun(TriviaGame game, ITextChannel channel, int savedId)
    {
        public TriviaGame Game { get; } = game;
        public ITextChannel Channel { get; } = channel;
        public int SavedId { get; } = savedId;
        public EmbedBuilder? QuestionEmbed { get; set; }
        public IUserMessage? QuestionMessage { get; set; }
        public bool ShowHowToQuit { get; set; }
    }
}
