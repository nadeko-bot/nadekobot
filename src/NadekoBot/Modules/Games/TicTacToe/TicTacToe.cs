#nullable disable
using CommandLine;
using System.Text;

namespace NadekoBot.Modules.Games.Common;

public class TicTacToe
{
    public event Action<TicTacToe> OnEnded;
    private readonly ITextChannel _channel;
    private readonly IGuildUser[] _users;
    private readonly int?[,] _state;
    private Phase phase;
    private int curUserIndex;
    private readonly SemaphoreSlim _moveLock;

    private IGuildUser winner;

    private readonly string[] _numbers =
    [
        ":one:", ":two:", ":three:", ":four:", ":five:", ":six:", ":seven:", ":eight:", ":nine:"
    ];

    private IUserMessage previousMessage;
    private CancellationTokenSource turnCts;
    private readonly IBotStrings _strings;
    private readonly DiscordSocketClient _client;
    private readonly Options _options;
    private readonly IMessageSenderService _sender;

    public TicTacToe(
        IBotStrings strings,
        DiscordSocketClient client,
        ITextChannel channel,
        IGuildUser firstUser,
        Options options,
        IMessageSenderService sender)
    {
        _channel = channel;
        _strings = strings;
        _client = client;
        _options = options;
        _sender = sender;

        _users = [firstUser, null];
        _state = new int?[,] { { null, null, null }, { null, null, null }, { null, null, null } };

        phase = Phase.Starting;
        _moveLock = new(1, 1);
    }

    private string GetText(LocStr key)
        => _strings.GetText(key, _channel.GuildId);

    public string GetState()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < _state.GetLength(0); i++)
        {
            for (var j = 0; j < _state.GetLength(1); j++)
            {
                sb.Append(_state[i, j] is null ? _numbers[(i * 3) + j] : GetIcon(_state[i, j]));
                if (j < _state.GetLength(1) - 1)
                    sb.Append("┃");
            }

            if (i < _state.GetLength(0) - 1)
                sb.AppendLine("\n──────────");
        }

        return sb.ToString();
    }

    public EmbedBuilder GetEmbed(string title = null)
    {
        var embed = _sender.CreateEmbed()
                       .WithOkColor()
                       .WithDescription(Environment.NewLine + GetState())
                       .WithAuthor(GetText(strs.vs(_users[0], _users[1])));

        if (!string.IsNullOrWhiteSpace(title))
            embed.WithTitle(title);

        if (winner is null)
        {
            if (phase == Phase.Ended)
                embed.WithFooter(GetText(strs.ttt_no_moves));
            else
                embed.WithFooter(GetText(strs.ttt_users_move(_users[curUserIndex])));
        }
        else
            embed.WithFooter(GetText(strs.ttt_has_won(winner)));

        return embed;
    }

    private static string GetIcon(int? val)
    {
        switch (val)
        {
            case 0:
                return "❌";
            case 1:
                return "⭕";
            case 2:
                return "❎";
            case 3:
                return "🅾";
            default:
                return "⬛";
        }
    }

    public async Task Start(IGuildUser user)
    {
        if (phase is Phase.Started or Phase.Ended)
        {
            await _sender.Response(_channel).Error(user.Mention + GetText(strs.ttt_already_running)).SendAsync();
            return;
        }

        if (_users[0] == user)
        {
            await _sender.Response(_channel).Error(user.Mention + GetText(strs.ttt_against_yourself)).SendAsync();
            return;
        }

        _users[1] = user;

        phase = Phase.Started;

        _client.MessageReceived += Client_MessageReceived;
        RestartTurnTimer();


        previousMessage = await _sender.Response(_channel).Embed(GetEmbed(GetText(strs.game_started))).SendAsync();
    }

    private void RestartTurnTimer()
    {
        var cts = new CancellationTokenSource();
        var old = Interlocked.Exchange(ref turnCts, cts);
        old?.Cancel();
        old?.Dispose();
        _ = TurnTimeoutInternalAsync(cts.Token);
    }

    private async Task TurnTimeoutInternalAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(_options.TurnTimer * 1000, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await _moveLock.WaitAsync();
        try
        {
            if (phase == Phase.Ended || token.IsCancellationRequested)
                return;

            winner = _users[curUserIndex ^= 1];
            EndInternal();

            var del = previousMessage?.DeleteAsync();
            await _sender.Response(_channel).Embed(GetEmbed(GetText(strs.ttt_time_expired))).SendAsync();
            if (del is not null)
                await del;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error ending a tic-tac-toe game after a turn timeout");
        }
        finally
        {
            _moveLock.Release();
        }
    }

    private void EndInternal()
    {
        phase = Phase.Ended;
        _client.MessageReceived -= Client_MessageReceived;
        OnEnded?.Invoke(this);
    }

    private bool IsDraw()
    {
        for (var i = 0; i < 3; i++)
        for (var j = 0; j < 3; j++)
        {
            if (_state[i, j] is null)
                return false;
        }

        return true;
    }

    private Task Client_MessageReceived(SocketMessage msg)
    {
        if (msg.Channel.Id != _channel.Id)
            return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            await _moveLock.WaitAsync();
            try
            {
                var curUser = _users[curUserIndex];
                if (phase == Phase.Ended || msg.Author?.Id != curUser.Id)
                    return;

                if (int.TryParse(msg.Content, out var index)
                    && --index >= 0
                    && index < 9
                    && _state[index / 3, index % 3] is null)
                {
                    _state[index / 3, index % 3] = curUserIndex;

                    // i'm lazy
                    if (_state[index / 3, 0] == _state[index / 3, 1] && _state[index / 3, 1] == _state[index / 3, 2])
                    {
                        _state[index / 3, 0] = curUserIndex + 2;
                        _state[index / 3, 1] = curUserIndex + 2;
                        _state[index / 3, 2] = curUserIndex + 2;

                        phase = Phase.Ended;
                    }
                    else if (_state[0, index % 3] == _state[1, index % 3]
                             && _state[1, index % 3] == _state[2, index % 3])
                    {
                        _state[0, index % 3] = curUserIndex + 2;
                        _state[1, index % 3] = curUserIndex + 2;
                        _state[2, index % 3] = curUserIndex + 2;

                        phase = Phase.Ended;
                    }
                    else if (curUserIndex == _state[0, 0]
                             && _state[0, 0] == _state[1, 1]
                             && _state[1, 1] == _state[2, 2])
                    {
                        _state[0, 0] = curUserIndex + 2;
                        _state[1, 1] = curUserIndex + 2;
                        _state[2, 2] = curUserIndex + 2;

                        phase = Phase.Ended;
                    }
                    else if (curUserIndex == _state[0, 2]
                             && _state[0, 2] == _state[1, 1]
                             && _state[1, 1] == _state[2, 0])
                    {
                        _state[0, 2] = curUserIndex + 2;
                        _state[1, 1] = curUserIndex + 2;
                        _state[2, 0] = curUserIndex + 2;

                        phase = Phase.Ended;
                    }

                    var reason = string.Empty;

                    if (phase == Phase.Ended) // if user won, stop receiving moves
                    {
                        reason = GetText(strs.ttt_matched_three);
                        winner = _users[curUserIndex];
                        EndInternal();
                    }
                    else if (IsDraw())
                    {
                        reason = GetText(strs.ttt_a_draw);
                        EndInternal();
                    }

                    _ = Task.Run(async () =>
                    {
                        var del1 = msg.DeleteAsync();
                        var del2 = previousMessage?.DeleteAsync();
                        try { previousMessage = await _sender.Response(_channel).Embed(GetEmbed(reason)).SendAsync(); }
                        catch { }

                        try { await del1; }
                        catch { }

                        try
                        {
                            if (del2 is not null)
                                await del2;
                        }
                        catch { }
                    });
                    curUserIndex ^= 1;

                    if (phase == Phase.Ended)
                        turnCts?.Cancel();
                    else
                        RestartTurnTimer();
                }
            }
            finally
            {
                _moveLock.Release();
            }
        });

        return Task.CompletedTask;
    }

    public class Options : INadekoCommandOptions
    {
        [Option('t', "turn-timer", Required = false, Default = 15, HelpText = "Turn time in seconds. Default 15.")]
        public int TurnTimer { get; set; } = 15;

        public void NormalizeOptions()
        {
            if (TurnTimer is < 5 or > 60)
                TurnTimer = 15;
        }
    }

    private enum Phase
    {
        Starting,
        Started,
        Ended
    }
}