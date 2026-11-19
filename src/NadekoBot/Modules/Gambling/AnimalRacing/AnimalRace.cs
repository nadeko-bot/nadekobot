using NadekoBot.Modules.Games.Common;

namespace NadekoBot.Modules.Gambling.Common.AnimalRacing;

// The members, the animals and the run. The service moves the currency and sends the messages.
public sealed class AnimalRace
{
    public const double BASE_MULTIPLIER = 0.87;
    public const double MAX_MULTIPLIER = 0.94;
    public const double MULTI_PER_USER = 0.005;
    public const int FINISH = 60;
    public static readonly TimeSpan StepDelay = TimeSpan.FromMilliseconds(1750);

    public enum Phase
    {
        WaitingForPlayers,
        Running,
        Ended
    }

    public event Func<AnimalRace, Task> OnStarted = static _ => Task.CompletedTask;
    public event Func<AnimalRace, Task> OnStartingFailed = static _ => Task.CompletedTask;
    public event Func<AnimalRace, Task> OnStateUpdate = static _ => Task.CompletedTask;
    public event Func<AnimalRace, Task> OnEnded = static _ => Task.CompletedTask;

    public Phase CurrentPhase { get; private set; } = Phase.WaitingForPlayers;

    public RaceOptions Options { get; }

    public IReadOnlyList<RaceAnimal> Animals { get; }

    public int MaxUsers
        => Animals.Count;

    // in join order; changes only before the start
    public IReadOnlyList<AnimalRacingUser> Users
        => _users;

    public List<AnimalRacingUser> FinishedUsers { get; } = new();

    public double Multi { get; private set; } = BASE_MULTIPLIER;

    public bool Failed { get; private set; }

    private readonly SemaphoreSlim _locker = new(1, 1);
    private readonly List<AnimalRacingUser> _users = new();

    public AnimalRace(RaceOptions options, IReadOnlyList<RaceAnimal> animals)
    {
        Options = options;
        Animals = animals;

        if (animals.Count == 0)
            CurrentPhase = Phase.Ended;
    }

    // a member who joined before a restart; call it before the race starts
    public bool AddRestored(ulong userId, string userName, long bet)
    {
        if (_users.Count == MaxUsers || FindInternal(userId) is not null)
            return false;

        _users.Add(new(userName, userId, bet, Animals[_users.Count]));
        return true;
    }

    public void Initialize()
        => _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(Options.StartTime));
            await StartLockedInternalAsync();
        });

    public void StartNow()
        => _ = Task.Run(StartLockedInternalAsync);

    // The commit runs under the race lock, so one join at a time moves currency.
    public async Task<(RaceJoinResult Result, AnimalRacingUser? User)> JoinAsync(
        ulong userId,
        string userName,
        long bet,
        Func<Task<RaceJoinResult>> commit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bet);

        await _locker.WaitAsync();
        try
        {
            if (CurrentPhase != Phase.WaitingForPlayers)
                return (RaceJoinResult.AlreadyStarted, null);

            if (_users.Count == MaxUsers)
                return (RaceJoinResult.Full, null);

            if (FindInternal(userId) is not null)
                return (RaceJoinResult.AlreadyJoined, null);

            var result = await commit();
            if (result != RaceJoinResult.Joined)
                return (result, null);

            var user = new AnimalRacingUser(userName, userId, bet, Animals[_users.Count]);
            _users.Add(user);

            if (_users.Count == MaxUsers)
                StartInternal();

            return (result, user);
        }
        finally
        {
            _locker.Release();
        }
    }

    private AnimalRacingUser? FindInternal(ulong userId)
    {
        foreach (var user in _users)
        {
            if (user.UserId == userId)
                return user;
        }

        return null;
    }

    private async Task StartLockedInternalAsync()
    {
        await _locker.WaitAsync();
        try
        {
            if (CurrentPhase == Phase.WaitingForPlayers)
                StartInternal();
        }
        finally
        {
            _locker.Release();
        }
    }

    // every path fires OnStartingFailed or OnEnded, because the service cleans up only on those events
    private void StartInternal()
    {
        CurrentPhase = Phase.Running;
        if (_users.Count <= 1)
        {
            CurrentPhase = Phase.Ended;
            _ = OnStartingFailed(this);
            return;
        }

        _ = OnStarted(this);
        _ = Task.Run(RunInternalAsync);
    }

    private async Task RunInternalAsync()
    {
        try
        {
            var rng = new NadekoRandom();
            var finishedNow = new List<AnimalRacingUser>(_users.Count);
            while (FinishedUsers.Count < _users.Count)
            {
                finishedNow.Clear();
                foreach (var user in _users)
                {
                    if (user.Progress >= FINISH)
                        continue;

                    user.Progress = Math.Min(FINISH, user.Progress + rng.Next(1, 10));
                    if (user.Progress == FINISH)
                        finishedNow.Add(user);
                }

                // members who cross the line in the same step get a random order
                FinishedUsers.AddRange(finishedNow.Shuffle());

                try
                {
                    await OnStateUpdate(this);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Animal race could not show the race");
                }

                await Task.Delay(StepDelay);
            }

            Multi = FinishedUsers.Count
                    * Math.Min(MAX_MULTIPLIER, BASE_MULTIPLIER + (MULTI_PER_USER * FinishedUsers.Count));
        }
        catch (Exception ex)
        {
            Failed = true;
            Log.Error(ex, "Animal race failed while running");
        }
        finally
        {
            CurrentPhase = Phase.Ended;
            _ = OnEnded(this);
        }
    }
}
