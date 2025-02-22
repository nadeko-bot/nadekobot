using System.Threading.Channels;
using Exception = System.Exception;

namespace NadekoBot.Modules.Games.Common.Trivia;

public sealed class TriviaGame
{
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

    private bool _isStopped;

    public TriviaQuestion? CurrentQuestion { get; set; }


    private readonly ConcurrentDictionary<ulong, int> _users = new();

    private readonly Channel<(TriviaUser User, string Input)> _inputs
        = Channel.CreateUnbounded<(TriviaUser, string)>(new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = true,
            SingleReader = true,
            SingleWriter = false,
        });

    public TriviaGame(TriviaOptions options, ILocalDataCache cache)
    {
        _opts = options;

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
        var inactivity = 0;

        // loop until game is stopped
        // each iteration is one round
        var firstRun = true;
        try
        {
            while (!_isStopped)
            {
                if (errorCount >= 5)
                {
                    Log.Warning("Trivia errored 5 times and will quit");
                    break;
                }

                // wait for 3 seconds before posting the next question
                if (firstRun)
                {
                    firstRun = false;
                }
                else
                {
                    await Task.Delay(3000);
                }

                var maybeQuestion = await _questionPool.GetQuestionAsync();

                if (maybeQuestion is not { } question)
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

                        // reset inactivity counter
                        inactivity = 0;
                        errorCount = 0;

                        var isWin = false;
                        // if user won the game, tell the game to stop
                        if (_opts.WinRequirement != 0 && val >= _opts.WinRequirement)
                        {
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
                    await OnTimeout(this, question);

                    if (_opts.Timeout != 0 && ++inactivity >= _opts.Timeout)
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
        => _users.Select(x => (x.Key, x.Value)).ToArray();

    public ValueTask InputAsync(TriviaUser user, string input)
        => _inputs.Writer.WriteAsync((user, input));

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
        if (CurrentQuestion is TriviaQuestion q)
            await OnQuestion(this, q);
    }
}