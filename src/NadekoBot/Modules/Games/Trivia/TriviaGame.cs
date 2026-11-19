using System.Threading.Channels;
using NadekoBot.Modules.Games.SavedGames;
using Exception = System.Exception;

namespace NadekoBot.Modules.Games.Common.Trivia;

public sealed class TriviaGame
{
    public const int MAX_SAVED_SCORES = 100;

    // answers which wait for the game loop; the game drops more
    public const int MAX_WAITING_ANSWERS = 64;

    private const int MAX_ERRORS = 5;
    private static readonly TimeSpan _nextQuestionDelay = TimeSpan.FromSeconds(3);

    private readonly TriviaOptions _opts;

    private readonly IQuestionPool _questionPool;

    #region Events

    public event Func<TriviaGame, TriviaQuestion, Task> OnQuestion = static delegate { return Task.CompletedTask; };
    public event Func<TriviaGame, TriviaQuestion, Task> OnHint = static delegate { return Task.CompletedTask; };
    public event Func<TriviaGame, Task> OnStats = static delegate { return Task.CompletedTask; };

    public event Func<TriviaGame, TriviaUser, TriviaQuestion, bool, Task> OnGuess = static delegate
    {
        return Task.CompletedTask;
    };

    public event Func<TriviaGame, TriviaQuestion, Task> OnTimeout = static delegate { return Task.CompletedTask; };
    public event Func<TriviaGame, Task> OnEnded = static delegate { return Task.CompletedTask; };

    #endregion

    private volatile bool _isStopped;
    private int _inactivity;
    private int _dropWarned;

    // the question which was open when the bot restarted; the game asks it first
    private TriviaQuestion? _restoredQuestion;

    public TriviaQuestion? CurrentQuestion { get; private set; }

    public ulong? WinnerId { get; private set; }

    private readonly ConcurrentDictionary<ulong, int> _users = new();

    private readonly Channel<(TriviaUser User, string Input)> _inputs
        = Channel.CreateBounded<(TriviaUser, string)>(new BoundedChannelOptions(MAX_WAITING_ANSWERS)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

    public ulong ChannelId { get; }

    public TriviaGame(TriviaOptions options, ILocalDataCache cache, ulong channelId)
    {
        _opts = options;
        ChannelId = channelId;

        _questionPool = _opts.IsPokemon
            ? new PokemonQuestionPool(cache)
            : new DefaultQuestionPool(cache);
    }

    public async Task RunAsync()
    {
        await GameLoop();
    }

    private async Task GameLoop()
    {
        Task TimeOutFactory()
            => Task.Delay(_opts.QuestionTimer * 1000 / 2);

        var errorCount = 0;

        // loop until game is stopped
        // each iteration is one round
        var firstRun = true;
        try
        {
            while (!_isStopped)
            {
                if (errorCount >= MAX_ERRORS)
                {
                    Log.Warning("Trivia errored {Count} times and will quit", MAX_ERRORS);
                    break;
                }

                if (firstRun)
                {
                    firstRun = false;
                }
                else
                {
                    await Task.Delay(_nextQuestionDelay);

                    if (_isStopped)
                        break;
                }

                var question = _restoredQuestion ?? await _questionPool.GetQuestionAsync();
                _restoredQuestion = null;

                if (question is null)
                {
                    // if question is null (ran out of question, or other bugg ) - stop
                    break;
                }

                CurrentQuestion = question;
                try
                {
                    // clear out all of the past guesses
                    while (_inputs.Reader.TryRead(out _))
                    {
                    }

                    Volatile.Write(ref _dropWarned, 0);
                    await OnQuestion(this, question);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error executing OnQuestion: {Message}", ex.Message);
                    errorCount++;
                    continue;
                }


                // just keep looping through user inputs until someone guesses the answer
                // or the timer expires
                var halfGuessTimerTask = TimeOutFactory();
                var hintSent = false;
                var guessed = false;
                while (true)
                {
                    using var readCancel = new CancellationTokenSource();
                    var readTask = _inputs.Reader.ReadAsync(readCancel.Token).AsTask();

                    // wait for either someone to attempt to guess
                    // or for timeout
                    var task = await Task.WhenAny(readTask, halfGuessTimerTask);

                    // if the task which completed is the timeout task
                    if (task == halfGuessTimerTask)
                    {
                        readCancel.Cancel();

                        // if hint is already sent, means time expired
                        // break (end the round)
                        if (hintSent)
                            break;

                        // else, means half time passed, send a hint
                        hintSent = true;
                        // start a new countdown of the same length
                        halfGuessTimerTask = TimeOutFactory();
                        if (!_opts.NoHint)
                        {
                            // send a hint out
                            await OnHint(this, question);
                        }

                        continue;
                    }

                    // otherwise, read task is successful, and we're gonna
                    // get the user input data
                    var (user, input) = await readTask;

                    // check the guess
                    if (question.IsAnswerCorrect(input))
                    {
                        // add 1 point to the user
                        var val = _users.AddOrUpdate(user.Id, 1, (_, points) => ++points);
                        guessed = true;

                        // an answered question is not open any more, so a restart asks a new one
                        CurrentQuestion = null;

                        // reset inactivity counter
                        Volatile.Write(ref _inactivity, 0);
                        errorCount = 0;

                        var isWin = false;
                        // if user won the game, tell the game to stop
                        if (_opts.WinRequirement != 0 && val >= _opts.WinRequirement)
                        {
                            WinnerId = user.Id;
                            _isStopped = true;
                            isWin = true;
                        }

                        // call onguess
                        await OnGuess(this, user, question, isWin);
                        break;
                    }
                }

                if (!guessed)
                {
                    CurrentQuestion = null;
                    await OnTimeout(this, question);

                    var inactivity = Interlocked.Increment(ref _inactivity);
                    if (_opts.Timeout != 0 && inactivity >= _opts.Timeout)
                    {
                        Log.Information("Trivia game is stopping due to inactivity");
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fatal error in trivia game: {ErrorMessage}", ex.Message);
        }
        finally
        {
            // make sure game is set as ended
            _isStopped = true;
            _ = OnEnded(this);
        }
    }

    public IReadOnlyList<(ulong User, int points)> GetLeaderboard()
    {
        var entries = _users.Select(x => (x.Key, x.Value)).ToArray();
        Array.Sort(entries, static (a, b) => b.Value.CompareTo(a.Value));
        return entries;
    }

    // Runs on the gateway thread, so it only queues the answer.
    public bool TryInput(TriviaUser user, string input)
    {
        if (_inputs.Writer.TryWrite((user, input)))
            return true;

        if (Interlocked.Exchange(ref _dropWarned, 1) == 0)
        {
            Log.Warning("Trivia in channel {ChannelId} drops answers, because {Count} answers wait",
                ChannelId,
                MAX_WAITING_ANSWERS);
        }

        return false;
    }

    public bool Stop()
    {
        var isStopped = _isStopped;
        _isStopped = true;
        return !isStopped;
    }

    public async ValueTask TriggerStatsAsync()
    {
        await OnStats(this);
    }

    public async Task TriggerQuestionAsync()
    {
        if (CurrentQuestion is { } q)
            await OnQuestion(this, q);
    }

    public byte[] Save()
    {
        var board = GetLeaderboard();
        var scores = new TriviaScoreSave[Math.Min(board.Count, MAX_SAVED_SCORES)];
        for (var i = 0; i < scores.Length; i++)
            scores[i] = new() { UserId = board[i].User, Points = board[i].points };

        return SavedGameJson.Write(new TriviaSave
            {
                V = TriviaSave.VERSION,
                Pokemon = _opts.IsPokemon,
                NoHint = _opts.NoHint,
                WinRequirement = _opts.WinRequirement,
                QuestionTimer = _opts.QuestionTimer,
                Timeout = _opts.Timeout,
                Inactivity = Volatile.Read(ref _inactivity),
                Stopped = _isStopped,
                Scores = scores,
                Question = CurrentQuestion?.Model
            },
            SavedGameJsonContext.Default.TriviaSave);
    }

    // null when the data is not a running game of this version
    public static TriviaGame? Load(byte[] data, ILocalDataCache cache, ulong channelId)
    {
        if (SavedGameJson.Read(data, SavedGameJsonContext.Default.TriviaSave)
            is not { V: TriviaSave.VERSION, Stopped: false } save)
            return null;

        var options = new TriviaOptions
        {
            IsPokemon = save.Pokemon,
            NoHint = save.NoHint,
            WinRequirement = save.WinRequirement,
            QuestionTimer = save.QuestionTimer,
            Timeout = save.Timeout
        };
        options.NormalizeOptions();

        var game = new TriviaGame(options, cache, channelId);
        if (save.Scores is { } scores)
        {
            var count = Math.Min(scores.Length, MAX_SAVED_SCORES);
            for (var i = 0; i < count; i++)
            {
                if (scores[i] is { Points: > 0 } score)
                    game._users[score.UserId] = score.Points;
            }
        }

        if (save.Question is { Question: not null, Answer: not null } question)
        {
            game._restoredQuestion = new TriviaQuestion(question);
            game.CurrentQuestion = game._restoredQuestion;
        }

        game._inactivity = Math.Max(0, save.Inactivity);
        return game;
    }
}
