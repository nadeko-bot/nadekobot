#nullable disable
using NadekoBot.Modules.Gambling.Common.AnimalRacing.Exceptions;
using NadekoBot.Modules.Games.Common;

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

    public AnimalRace(RaceOptions options, ICurrencyService currency, IEnumerable<RaceAnimal> availableAnimals)
    {
        _currency = currency;
        _options = options;
        _animalsQueue = new(availableAnimals);
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
            finally { _locker.Release(); }
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

            if (!await _currency.RemoveAsync(userId, bet, new("animalrace", "bet")))
                throw new NotEnoughFundsException();

            if (_users.Contains(user))
                throw new AlreadyJoinedException();

            var animal = _animalsQueue.Dequeue();
            user.Animal = animal;
            _users.Add(user);

            if (_animalsQueue.Count == 0) //start if no more spots left
                await Start();

            return user;
        }
        finally { _locker.Release(); }
    }

    private async Task Start()
    {
        CurrentPhase = Phase.Running;
        if (_users.Count <= 1)
        {
            foreach (var user in _users)
            {
                if (user.Bet > 0)
                    await _currency.AddAsync(user.UserId, (long)(user.Bet * BASE_MULTIPLIER), new("animalrace", "refund"));
            }

            _ = OnStartingFailed?.Invoke(this);
            CurrentPhase = Phase.Ended;
            return;
        }

        _ = OnStarted?.Invoke(this);
        _ = Task.Run(async () =>
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

            _ = OnEnded?.Invoke(this);
        });
    }

    public double Multi { get; set; } = BASE_MULTIPLIER;

    public void Dispose()
    {
        CurrentPhase = Phase.Ended;
        OnStarted = null;
        OnEnded = null;
        OnStartingFailed = null;
        OnStateUpdate = null;
        _locker.Dispose();
        _users.Clear();
    }
}