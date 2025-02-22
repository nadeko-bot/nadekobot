#nullable disable
using System.Collections.Immutable;

namespace NadekoBot.Modules.Games.Common.Nunchi;

public sealed class NunchiGame : IDisposable
{
    public enum Phase
    {
        Joining,
        Playing,
        WaitingForNextRound,
        Ended
    }

    private const int KILL_TIMEOUT = 20 * 1000;
    private const int NEXT_ROUND_TIMEOUT = 5 * 1000;

    public event Func<NunchiGame, Task> OnGameStarted;
    public event Func<NunchiGame, int, Task> OnRoundStarted;
    public event Func<NunchiGame, Task> OnUserGuessed;
    public event Func<NunchiGame, (ulong Id, string Name)?, Task> OnRoundEnded; // tuple of the user who failed
    public event Func<NunchiGame, string, Task> OnGameEnded; // name of the user who won

    public int CurrentNumber { get; private set; } = new NadekoRandom().Next(0, 100);
    public Phase CurrentPhase { get; private set; } = Phase.Joining;

    public ImmutableArray<(ulong Id, string Name)> Participants
        => participants.ToImmutableArray();

    public int ParticipantCount
        => participants.Count;

    private readonly SemaphoreSlim _locker = new(1, 1);

    private HashSet<(ulong Id, string Name)> participants = [];
    private readonly HashSet<(ulong Id, string Name)> _passed = [];
    private Timer killTimer;

    public NunchiGame(ulong creatorId, string creatorName)
        => participants.Add((creatorId, creatorName));

    public async Task<bool> Join(ulong userId, string userName)
    {
        await _locker.WaitAsync();
        try
        {
            if (CurrentPhase != Phase.Joining)
                return false;

            return participants.Add((userId, userName));
        }
        finally { _locker.Release(); }
    }

    public async Task<bool> Initialize()
    {
        CurrentPhase = Phase.Joining;
        await Task.Delay(30000);
        await _locker.WaitAsync();
        try
        {
            if (participants.Count < 3)
            {
                CurrentPhase = Phase.Ended;
                return false;
            }

            killTimer = new(async _ =>
                {
                    await _locker.WaitAsync();
                    try
                    {
                        if (CurrentPhase != Phase.Playing)
                            return;

                        //if some players took too long to type a number, boot them all out and start a new round
                        participants = new HashSet<(ulong, string)>(_passed);
                        EndRound();
                    }
                    finally { _locker.Release(); }
                },
                null,
                KILL_TIMEOUT,
                KILL_TIMEOUT);

            CurrentPhase = Phase.Playing;
            _ = OnGameStarted?.Invoke(this);
            _ = OnRoundStarted?.Invoke(this, CurrentNumber);
            return true;
        }
        finally { _locker.Release(); }
    }

    public async Task Input(ulong userId, string userName, int input)
    {
        await _locker.WaitAsync();
        try
        {
            if (CurrentPhase != Phase.Playing)
                return;

            var userTuple = (Id: userId, Name: userName);

            // if the user is not a member of the race,
            // or he already successfully typed the number
            // ignore the input
            if (!participants.Contains(userTuple) || !_passed.Add(userTuple))
                return;

            //if the number is correct
            if (CurrentNumber == input - 1)
            {
                //increment current number
                ++CurrentNumber;
                if (_passed.Count == participants.Count - 1)
                {
                    // if only n players are left, and n - 1 type the correct number, round is over

                    // if only 2 players are left, game is over
                    if (participants.Count == 2)
                    {
                        killTimer.Change(Timeout.Infinite, Timeout.Infinite);
                        CurrentPhase = Phase.Ended;
                        _ = OnGameEnded?.Invoke(this, userTuple.Name);
                    }
                    else // else just start the new round without the user who was the last
                    {
                        var failure = participants.Except(_passed).First();

                        OnUserGuessed?.Invoke(this);
                        EndRound(failure);
                        return;
                    }
                }

                OnUserGuessed?.Invoke(this);
            }
            else
            {
                //if the user failed

                EndRound(userTuple);
            }
        }
        finally { _locker.Release(); }
    }

    private void EndRound((ulong, string)? failure = null)
    {
        killTimer.Change(KILL_TIMEOUT, KILL_TIMEOUT);
        CurrentNumber = new NadekoRandom().Next(0, 100); // reset the counter
        _passed.Clear(); // reset all users who passed (new round starts)
        if (failure is not null)
            participants.Remove(failure.Value); // remove the dude who failed from the list of players

        _ = OnRoundEnded?.Invoke(this, failure);
        if (participants.Count <= 1) // means we have a winner or everyone was booted out
        {
            killTimer.Change(Timeout.Infinite, Timeout.Infinite);
            CurrentPhase = Phase.Ended;
            _ = OnGameEnded?.Invoke(this, participants.Count > 0 ? participants.First().Name : null);
            return;
        }

        CurrentPhase = Phase.WaitingForNextRound;
        Task.Run(async () =>
        {
            await Task.Delay(NEXT_ROUND_TIMEOUT);
            CurrentPhase = Phase.Playing;
            _ = OnRoundStarted?.Invoke(this, CurrentNumber);
        });
    }

    public void Dispose()
    {
        OnGameEnded = null;
        OnGameStarted = null;
        OnRoundEnded = null;
        OnRoundStarted = null;
        OnUserGuessed = null;
    }
}