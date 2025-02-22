#nullable disable
using Nadeko.Econ;

namespace NadekoBot.Modules.Gambling.Common.Blackjack;

public class Blackjack
{
    public enum GameState
    {
        Starting,
        Playing,
        Ended
    }

    public event Func<Blackjack, Task> StateUpdated;
    public event Func<Blackjack, Task> GameEnded;

    private Deck Deck { get; } = new QuadDeck();
    public Dealer Dealer { get; set; }


    public List<User> Players { get; set; } = new();
    public GameState State { get; set; } = GameState.Starting;
    public User CurrentUser { get; private set; }

    private TaskCompletionSource<bool> currentUserMove;
    private readonly ICurrencyService _cs;

    private readonly SemaphoreSlim _locker = new(1, 1);

    public Blackjack(ICurrencyService cs)
    {
        _cs = cs;
        Dealer = new();
    }

    public void Start()
        => _ = GameLoop();

    public async Task GameLoop()
    {
        try
        {
            //wait for players to join
            await Task.Delay(20000);
            await _locker.WaitAsync();
            try
            {
                State = GameState.Playing;
            }
            finally
            {
                _locker.Release();
            }

            await PrintState();
            //if no users joined the game, end it
            if (!Players.Any())
            {
                State = GameState.Ended;
                _ = GameEnded?.Invoke(this);
                return;
            }

            //give 1 card to the dealer and 2 to each player
            Dealer.Cards.Add(Deck.Draw());
            foreach (var usr in Players)
            {
                usr.Cards.Add(Deck.Draw());
                usr.Cards.Add(Deck.Draw());

                if (usr.GetHandValue() == 21)
                    usr.State = User.UserState.Blackjack;
            }

            //go through all users and ask them what they want to do
            foreach (var usr in Players.Where(x => !x.Done))
            {
                while (!usr.Done)
                {
                    Log.Information("Waiting for {DiscordUser}'s move", usr.DiscordUser);
                    await PromptUserMove(usr);
                }
            }

            await PrintState();
            State = GameState.Ended;
            await Task.Delay(2500);
            Log.Information("Dealer moves");
            await DealerMoves();
            await PrintState();
            _ = GameEnded?.Invoke(this);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "REPORT THE MESSAGE BELOW IN #NadekoLog SERVER PLEASE");
            State = GameState.Ended;
            _ = GameEnded?.Invoke(this);
        }
    }

    private async Task PromptUserMove(User usr)
    {
        using var cts = new CancellationTokenSource();
        var pause = Task.Delay(20000, cts.Token); //10 seconds to decide
        CurrentUser = usr;
        currentUserMove = new();
        await PrintState();
        // either wait for the user to make an action and
        // if he doesn't - stand
        var finished = await Task.WhenAny(pause, currentUserMove.Task);
        if (finished == pause)
            await Stand(usr);
        else
            cts.Cancel();

        CurrentUser = null;
        currentUserMove = null;
    }

    public async Task<bool> Join(IUser user, long bet)
    {
        await _locker.WaitAsync();
        try
        {
            if (State != GameState.Starting)
                return false;

            if (Players.Count >= 5)
                return false;

            if (!await _cs.RemoveAsync(user, bet, new("blackjack", "gamble")))
                return false;

            Players.Add(new(user, bet));
            _ = PrintState();
            return true;
        }
        finally
        {
            _locker.Release();
        }
    }

    public async Task<bool> Stand(IUser u)
    {
        var cu = CurrentUser;

        if (cu is not null && cu.DiscordUser == u)
            return await Stand(cu);

        return false;
    }

    public async Task<bool> Stand(User u)
    {
        await _locker.WaitAsync();
        try
        {
            if (State != GameState.Playing)
                return false;

            if (CurrentUser != u)
                return false;

            u.State = User.UserState.Stand;
            currentUserMove.TrySetResult(true);
            return true;
        }
        finally
        {
            _locker.Release();
        }
    }

    private async Task DealerMoves()
    {
        var hw = Dealer.GetHandValue();
        while (hw < 17
               || (hw == 17
                   && Dealer.Cards.Count(x => x.Number == 1) > (Dealer.GetRawHandValue() - 17) / 10)) // hit on soft 17
        {
            /* Dealer has
                 A 6
                 That's 17, soft
                 hw == 17 => true
                 number of aces = 1
                 1 > 17-17 /10 => true
                
                 AA 5
                 That's 17, again soft, since one ace is worth 11, even though another one is 1
                 hw == 17 => true
                 number of aces = 2
                 2 > 27 - 17 / 10 => true

                 AA Q 5
                 That's 17, but not soft, since both aces are worth 1
                 hw == 17 => true
                 number of aces = 2
                 2 > 37 - 17 / 10 => false
             * */
            Dealer.Cards.Add(Deck.Draw());
            hw = Dealer.GetHandValue();
        }

        if (hw > 21)
        {
            foreach (var usr in Players)
            {
                if (usr.State is User.UserState.Stand or User.UserState.Blackjack)
                    usr.State = User.UserState.Won;
                else
                    usr.State = User.UserState.Lost;
            }
        }
        else
        {
            foreach (var usr in Players)
            {
                if (usr.State == User.UserState.Blackjack)
                    usr.State = User.UserState.Won;
                else if (usr.State == User.UserState.Stand)
                    usr.State = hw < usr.GetHandValue() ? User.UserState.Won : User.UserState.Lost;
                else
                    usr.State = User.UserState.Lost;
            }
        }

        foreach (var usr in Players)
        {
            if (usr.State is User.UserState.Won or User.UserState.Blackjack)
                await _cs.AddAsync(usr.DiscordUser.Id, usr.Bet * 2, new("blackjack", "win"));
        }
    }

    public async Task<bool> Double(IUser u)
    {
        var cu = CurrentUser;

        if (cu is not null && cu.DiscordUser == u)
            return await Double(cu);

        return false;
    }

    public async Task<bool> Double(User u)
    {
        await _locker.WaitAsync();
        try
        {
            if (State != GameState.Playing)
                return false;

            if (CurrentUser != u)
                return false;

            if (!await _cs.RemoveAsync(u.DiscordUser.Id, u.Bet, new("blackjack", "double")))
                return false;

            u.Bet *= 2;

            u.Cards.Add(Deck.Draw());

            if (u.GetHandValue() == 21)
                //blackjack
                u.State = User.UserState.Blackjack;
            else if (u.GetHandValue() > 21)
                // user busted
                u.State = User.UserState.Bust;
            else
                //with double you just get one card, and then you're done
                u.State = User.UserState.Stand;
            currentUserMove.TrySetResult(true);

            return true;
        }
        finally
        {
            _locker.Release();
        }
    }

    public async Task<bool> Hit(IUser u)
    {
        var cu = CurrentUser;

        if (cu is not null && cu.DiscordUser == u)
            return await Hit(cu);

        return false;
    }

    public async Task<bool> Hit(User u)
    {
        await _locker.WaitAsync();
        try
        {
            if (State != GameState.Playing)
                return false;

            if (CurrentUser != u)
                return false;

            u.Cards.Add(Deck.Draw());

            if (u.GetHandValue() == 21)
                //blackjack
                u.State = User.UserState.Blackjack;
            else if (u.GetHandValue() > 21)
                // user busted
                u.State = User.UserState.Bust;

            currentUserMove.TrySetResult(true);

            return true;
        }
        finally
        {
            _locker.Release();
        }
    }

    public Task PrintState()
    {
        if (StateUpdated is null)
            return Task.CompletedTask;
        return StateUpdated.Invoke(this);
    }
}