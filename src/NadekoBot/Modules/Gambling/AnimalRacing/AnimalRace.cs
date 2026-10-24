#nullable disable
using NadekoBot.Modules.Gambling.Common.AnimalRacing.Exceptions;
using NadekoBot.Modules.Games.Common;
using NadekoBot.Modules.Games.Quests;

namespace NadekoBot.Modules.Gambling.Common.AnimalRacing;

public sealed class AnimalRace : IDisposable
{
    public const double BASE_MULTIPLIER = 0.87;
    public const double MAX_MULTIPLIER = 0.94;
    public const double MULTI_PER_USER = 0.005;

    public enum Phase
    {
        WaitingForPlayers,
        Running,
        Ended
    }

    public event Func<AnimalRace, Task> OnStarted = delegate { return Task.CompletedTask; };
    public event Func<AnimalRace, Task> OnStartingFailed = delegate { return Task.CompletedTask; };
    public event Func<AnimalRace, Task> OnStateUpdate = delegate { return Task.CompletedTask; };
    public event Func<AnimalRace, Task> OnEnded = delegate { return Task.CompletedTask; };

    public Phase CurrentPhase { get; private set; } = Phase.WaitingForPlayers;

    public IReadOnlyCollection<AnimalRacingUser> Users
        => _users.ToList();

    public List<AnimalRacingUser> FinishedUsers { get; } = new();
    public int MaxUsers { get; }

    private readonly SemaphoreSlim _locker = new(1, 1);
    private readonly HashSet<AnimalRacingUser> _users = new();
    private readonly ICurrencyService _currency;
    private readonly RaceOptions _options;
    private readonly Queue<RaceAnimal> _animalsQueue;
    private readonly QuestService _quests;

    public AnimalRace(
        RaceOptions options,
        ICurrencyService currency,
        IEnumerable<RaceAnimal> availableAnimals,
        QuestService quests)
    {
        _currency = currency;
        _options = options;
        _animalsQueue = new(availableAnimals);
        _quests = quests;
        MaxUsers = _animalsQueue.Count;

        if (_animalsQueue.Count == 0)
            CurrentPhase = Phase.Ended;
    }

    public void Initialize() //lame name
        => _ = Task.Run(async () =>
        {
            await Task.Delay(_options.StartTime * 1000);

            await _locker.WaitAsync();
            try
            {
                if (CurrentPhase != Phase.WaitingForPlayers)
                    return;

                await Start();
            }
            finally
            {
                _locker.Release();
            }
        });

    public async Task<AnimalRacingUser> JoinRace(ulong userId, string userName, long bet = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bet);

        var user = new AnimalRacingUser(userName, userId, bet);

        await _locker.WaitAsync();
        try
        {
            if (_users.Count == MaxUsers)
                throw new AnimalRaceFullException();

            if (CurrentPhase != Phase.WaitingForPlayers)
                throw new AlreadyStartedException();

            if (_users.Contains(user))
                throw new AlreadyJoinedException();

            if (!await _currency.RemoveAsync(userId, bet, new("animalrace", "bet")))
                throw new NotEnoughFundsException();

            var animal = _animalsQueue.Dequeue();
            user.Animal = animal;
            _users.Add(user);

            if (_animalsQueue.Count == 0) //start if no more spots left
                await Start();

            return user;
        }
        finally
        {
            _locker.Release();
        }
    }

    // every path must fire OnStartingFailed or OnEnded, the commands clean up only on those events
    private async Task Start()
    {
        CurrentPhase = Phase.Running;
        if (_users.Count <= 1)
        {
            foreach (var user in _users)
            {
                if (user.Bet <= 0)
                    continue;

                try
                {
                    await _currency.AddAsync(user.UserId, user.Bet, new("animalrace", "refund"));
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Animal race refund of {Amount} to {UserId} failed", user.Bet, user.UserId);
                }
            }

            CurrentPhase = Phase.Ended;
            _ = OnStartingFailed?.Invoke(this);
            return;
        }

        try
        {
            foreach (var user in _users)
                await _quests.ReportActionAsync(user.UserId, QuestEventType.RaceJoined);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Animal race could not report the race quest");
        }

        _ = OnStarted?.Invoke(this);
        _ = Task.Run(RunInternalAsync);
    }

    private async Task RunInternalAsync()
    {
        try
        {
            var rng = new NadekoRandom();
            while (!_users.All(x => x.Progress >= 60))
            {
                foreach (var user in _users)
                {
                    user.Progress += rng.Next(1, 10);
                    if (user.Progress >= 60)
                        user.Progress = 60;
                }

                var finished = _users.Where(x => x.Progress >= 60 && !FinishedUsers.Contains(x)).Shuffle();

                FinishedUsers.AddRange(finished);

                _ = OnStateUpdate?.Invoke(this);
                await Task.Delay(1750);
            }

            if (FinishedUsers[0].Bet > 0)
            {
                Multi = FinishedUsers.Count
                        * Math.Min(MAX_MULTIPLIER, BASE_MULTIPLIER + (MULTI_PER_USER * FinishedUsers.Count));
                await _currency.AddAsync(FinishedUsers[0].UserId,
                    (long)(FinishedUsers[0].Bet * Multi),
                    new("animalrace", "win"));
            }
        }
        catch (Exception ex)
        {
            Failed = true;
            Log.Error(ex, "Animal race failed while running");
        }
        finally
        {
            CurrentPhase = Phase.Ended;
            _ = OnEnded?.Invoke(this);
        }
    }

    public double Multi { get; set; } = BASE_MULTIPLIER;

    public bool Failed { get; private set; }

    public void Dispose()
    {
        CurrentPhase = Phase.Ended;
        OnStarted = null;
        OnEnded = null;
        OnStartingFailed = null;
        OnStateUpdate = null;
        // the semaphore is not disposed: OnStartingFailed runs while it is held, and the holder still releases it
        _users.Clear();
    }
}