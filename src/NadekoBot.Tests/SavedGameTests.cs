#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Discord.WebSocket;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NadekoBot.Common;
using NadekoBot.Db.Models;
using NadekoBot.Extensions;
using NadekoBot.Modules.Games;
using NadekoBot.Modules.Games.Common.Trivia;
using NadekoBot.Modules.Games.Hangman;
using NadekoBot.Modules.Games.SavedGames;
using NadekoBot.Services;
using NadekoBot.Services.Currency;
using NadekoBot.Tests.Waifu;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests;

public sealed class SavedGameTests
{
    private const ulong GUILD_SHARD_0 = 4194304UL * 2;
    private const ulong GUILD_SHARD_1 = 4194304UL * 3;
    private const ulong CHANNEL = 10;
    private const ulong USER_A = 1;
    private const ulong USER_B = 2;

    private TestDbService _db = null!;
    private FakeTimeProvider _time = null!;
    private SavedGameStore _store = null!;

    [SetUp]
    public void Setup()
    {
        _db = new TestDbService();
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        _store = new SavedGameStore(_db, Substitute.For<ITxTracker>(), _time);
    }

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    [Test]
    public async Task Store_KeepsTheServerLimitBatchesUpdatesAndPaysAnEndOneTime()
    {
        const int limit = 2;
        var first = await _store.AddAsync(GUILD_SHARD_0, CHANNEL, SavedGameKind.Hangman, limit, [1]);
        Assert.That(first.Result, Is.EqualTo(SavedGameAddResult.Added));
        Assert.That((await _store.AddAsync(GUILD_SHARD_0, CHANNEL, SavedGameKind.Hangman, limit, [1])).Result,
            Is.EqualTo(SavedGameAddResult.AlreadyRunning));
        Assert.That((await _store.AddAsync(GUILD_SHARD_0, CHANNEL + 1, SavedGameKind.Hangman, limit, [1])).Result,
            Is.EqualTo(SavedGameAddResult.Added));
        Assert.That((await _store.AddAsync(GUILD_SHARD_0, CHANNEL + 2, SavedGameKind.Hangman, limit, [1])).Result,
            Is.EqualTo(SavedGameAddResult.LimitReached));

        // the limit is for each kind of game, and each server has its own
        Assert.That((await _store.AddAsync(GUILD_SHARD_0, CHANNEL, SavedGameKind.Trivia, 1, [1])).Result,
            Is.EqualTo(SavedGameAddResult.Added));
        Assert.That((await _store.AddAsync(GUILD_SHARD_1, CHANNEL + 3, SavedGameKind.Hangman, limit, [1])).Result,
            Is.EqualTo(SavedGameAddResult.Added));

        // the newest state waits for the batch
        _time.Advance(TimeSpan.FromSeconds(3));
        _store.Update(first.Id, [2]);
        _store.Update(first.Id, [3]);
        Assert.That((await _store.LoadAsync(2, 0)).Single(x => x.Id == first.Id).State, Is.EqualTo(new byte[] { 1 }));
        Assert.That(await _store.FlushAsync(), Is.EqualTo(1));

        var shardGames = await _store.LoadAsync(2, 0);
        Assert.That(shardGames, Has.Length.EqualTo(3));
        Assert.That(shardGames.Single(x => x.Id == first.Id).State, Is.EqualTo(new byte[] { 3 }));
        Assert.That(shardGames.Single(x => x.Id == first.Id).DateAdded, Is.EqualTo(_time.GetUtcNow().UtcDateTime));

        // an end pays one time, and a late update does not bring the game back
        var txData = new TxData("hangman", "win");
        Assert.That(await _store.EndAsync(first.Id, USER_A, 50, txData), Is.True);
        Assert.That(await _store.EndAsync(first.Id, USER_A, 50, txData), Is.False);
        _store.Update(first.Id, [4]);
        await _store.FlushAsync();

        Assert.That(await BalanceAsync(USER_A), Is.EqualTo(50));
        Assert.That((await _store.LoadAsync(1, 0)).Any(x => x.Id == first.Id), Is.False);
    }

    [Test]
    public async Task HangmanAndTrivia_ContinueFromTheSavedStateAndOldOrUnplayableGamesAreDropped()
    {
        var hangman = new HangmanGame(new() { Word = "Cat", ImageUrl = "" }, "animals");
        hangman.Guess("c");
        hangman.Guess("z");
        var hangmanRow = await _store.AddAsync(GUILD_SHARD_0,
            CHANNEL,
            SavedGameKind.Hangman,
            HangmanService.MAX_GAMES_PER_SERVER,
            hangman.Save());
        hangman.Guess("a");
        _store.Update(hangmanRow.Id, hangman.Save());
        await _store.FlushAsync();

        var rows = await _store.LoadAsync(1, 0);
        var loaded = HangmanGame.Load(rows.Single().State);
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.GetState().Word, Is.EqualTo("C a _ "));
        Assert.That(loaded.GetState().MissedLetters, Is.EqualTo(new[] { 'z' }));
        Assert.That(loaded.Guess("A").GuessResult, Is.EqualTo(HangmanGame.GuessResult.AlreadyTried));
        Assert.That(loaded.Guess("t").GuessResult, Is.EqualTo(HangmanGame.GuessResult.Win));

        // a fresh game continues, and the same game after the age limit is dropped
        var handler = Substitute.For<ISavedGameHandler>();
        handler.Kind.Returns(SavedGameKind.Hangman);
        handler.RestoreAsync(Arg.Any<SavedGame>()).Returns(SavedGameRestoreResult.Restored);
        handler.DropAsync(Arg.Any<SavedGame>()).Returns(ci => _store.EndAsync(ci.Arg<SavedGame>().Id));
        var ageRestorer = new SavedGameRestoreService(_store, null!, [handler], _time);

        Assert.That(await ageRestorer.RestoreAsync(rows), Is.EqualTo(1));
        _time.Advance(SavedGameRestoreService.MaxAge + TimeSpan.FromMinutes(1));
        Assert.That(await ageRestorer.RestoreAsync(rows), Is.Zero);
        Assert.That(await _store.LoadAsync(1, 0), Is.Empty);

        // the saved scores count and the open question comes first
        var triviaState = SavedGameJson.Write(new TriviaSave
            {
                V = TriviaSave.VERSION,
                WinRequirement = 3,
                QuestionTimer = 30,
                Timeout = 10,
                Scores = [new() { UserId = USER_A, Points = 2 }, new() { UserId = USER_B, Points = 1 }],
                Question = new() { Category = "geo", Question = "Capital of France?", Answer = "Paris" }
            },
            SavedGameJsonContext.Default.TriviaSave);

        var cache = Substitute.For<ILocalDataCache>();
        var trivia = TriviaGame.Load(TriviaGame.Load(triviaState, cache, CHANNEL)!.Save(), cache, CHANNEL);
        Assert.That(trivia, Is.Not.Null);

        var asked = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        trivia!.OnQuestion += (game, question) =>
        {
            asked.TrySetResult(question.Question);
            game.TryInput(new("b", USER_B), "");
            game.TryInput(new("b", USER_B), "!!");
            game.TryInput(new("a", USER_A), " PARIS ");
            return Task.CompletedTask;
        };
        trivia.OnEnded += _ =>
        {
            ended.TrySetResult();
            return Task.CompletedTask;
        };

        _ = trivia.RunAsync();
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.That(await asked.Task.WaitAsync(TimeSpan.FromSeconds(1)), Is.EqualTo("Capital of France?"));
        Assert.That(trivia.WinnerId, Is.EqualTo(USER_A));
        Assert.That(trivia.GetLeaderboard(), Is.EqualTo(new[] { (USER_A, 3), (USER_B, 1) }));

        // an empty answer never matches, also when the answer is only a stop word
        var stopWord = new TriviaQuestion(new TriviaQuestionModel { Answer = "The" });
        Assert.That((stopWord.IsAnswerCorrect(""), stopWord.IsAnswerCorrect("!!"), stopWord.IsAnswerCorrect(" THE ")),
            Is.EqualTo((false, false, true)));

        // the real service drops a trivia game which it cannot continue
        Assert.That((await _store.AddAsync(GUILD_SHARD_0,
                CHANNEL,
                SavedGameKind.Trivia,
                TriviaGamesService.MAX_GAMES_PER_SERVER,
                triviaState)).Result,
            Is.EqualTo(SavedGameAddResult.Added));

        var triviaService = new TriviaGamesService(Substitute.For<DiscordSocketClient>(),
            cache,
            Substitute.For<IMessageSenderService>(),
            Substitute.For<IBotStrings>(),
            Substitute.For<ICommandHandler>(),
            null!,
            _store,
            IntentState.None);
        var restorer = new SavedGameRestoreService(_store, null!, [triviaService], _time);

        Assert.That(await restorer.RestoreAsync(await _store.LoadAsync(1, 0)), Is.Zero);
        Assert.That(await _store.LoadAsync(1, 0), Is.Empty);
    }
    private async Task<long> BalanceAsync(ulong userId)
    {
        await using var ctx = _db.GetDbContext();
        return await ctx.GetTable<DiscordUser>()
            .Where(x => x.UserId == userId)
            .Select(x => x.CurrencyAmount)
            .FirstAsyncLinqToDB();
    }
}
