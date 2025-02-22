#nullable disable
namespace NadekoBot.Modules.Gambling.Common;

public class RollDuelGame
{
    public enum Reason
    {
        Normal,
        NoFunds,
        Timeout
    }

    public enum State
    {
        Waiting,
        Running,
        Ended
    }

    public event Func<RollDuelGame, Task> OnGameTick;
    public event Func<RollDuelGame, Reason, Task> OnEnded;

    public ulong P1 { get; }
    public ulong P2 { get; }

    public long Amount { get; }

    public List<(int, int)> Rolls { get; } = new();
    public State CurrentState { get; private set; }
    public ulong Winner { get; private set; }

    private readonly ulong _botId;

    private readonly ICurrencyService _cs;

    private readonly Timer _timeoutTimer;
    private readonly NadekoRandom _rng = new();
    private readonly SemaphoreSlim _locker = new(1, 1);

    public RollDuelGame(
        ICurrencyService cs,
        ulong botId,
        ulong p1,
        ulong p2,
        long amount)
    {
        P1 = p1;
        P2 = p2;
        _botId = botId;
        Amount = amount;
        _cs = cs;

        _timeoutTimer = new(async delegate
            {
                await _locker.WaitAsync();
                try
                {
                    if (CurrentState != State.Waiting)
                        return;
                    CurrentState = State.Ended;
                    await OnEnded?.Invoke(this, Reason.Timeout);
                }
                catch { }
                finally
                {
                    _locker.Release();
                }
            },
            null,
            TimeSpan.FromSeconds(15),
            TimeSpan.FromMilliseconds(-1));
    }

    public async Task StartGame()
    {
        await _locker.WaitAsync();
        try
        {
            if (CurrentState != State.Waiting)
                return;
            _timeoutTimer.Change(Timeout.Infinite, Timeout.Infinite);
            CurrentState = State.Running;
        }
        finally
        {
            _locker.Release();
        }

        if (!await _cs.RemoveAsync(P1, Amount, new("rollduel", "bet")))
        {
            await OnEnded?.Invoke(this, Reason.NoFunds);
            CurrentState = State.Ended;
            return;
        }

        if (!await _cs.RemoveAsync(P2, Amount, new("rollduel", "bet")))
        {
            await _cs.AddAsync(P1, Amount, new("rollduel", "refund"));
            await OnEnded?.Invoke(this, Reason.NoFunds);
            CurrentState = State.Ended;
            return;
        }

        int n1, n2;
        do
        {
            n1 = _rng.Next(0, 5);
            n2 = _rng.Next(0, 5);
            Rolls.Add((n1, n2));
            if (n1 != n2)
            {
                if (n1 > n2)
                    Winner = P1;
                else
                    Winner = P2;
                var won = (long)(Amount * 2 * 0.98f);
                await _cs.AddAsync(Winner, won, new("rollduel", "win"));

                await _cs.AddAsync(_botId, (Amount * 2) - won, new("rollduel", "fee"));
            }

            try { await OnGameTick?.Invoke(this); }
            catch { }

            await Task.Delay(2500);
            if (n1 != n2)
                break;
        } while (true);

        CurrentState = State.Ended;
        await OnEnded?.Invoke(this, Reason.Normal);
    }
}

public struct RollDuelChallenge
{
    public ulong Player1 { get; set; }
    public ulong Player2 { get; set; }
}