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
    private Timer timeoutTimer;
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

        timeoutTimer = new(async _ =>
            {
                await _moveLock.WaitAsync();
                try
                {
                    if (phase == Phase.Ended)
                        return;

                    phase = Phase.Ended;
                    if (_users[1] is not null)
                    {
                        winner = _users[curUserIndex ^= 1];
                        var del = previousMessage?.DeleteAsync();
                        try
                        {
                            await _sender.Response(_channel).Embed(GetEmbed(GetText(strs.ttt_time_expired))).SendAsync();
                            if (del is not null)
                                await del;
                        }
                        catch { }
                    }

                    OnEnded?.Invoke(this);
                }
                catch { }
                finally
                {
                    _moveLock.Release();
                }
            },
            null,
            _options.TurnTimer * 1000,
            Timeout.Infinite);

        _client.MessageReceived += Client_MessageReceived;


        previousMessage = await _sender.Response(_channel).Embed(GetEmbed(GetText(strs.game_started))).SendAsync();
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
                    && index <= 9
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
                        _client.MessageReceived -= Client_MessageReceived;
                        OnEnded?.Invoke(this);
                    }
                    else if (IsDraw())
                    {
                        reason = GetText(strs.ttt_a_draw);
                        phase = Phase.Ended;
                        _client.MessageReceived -= Client_MessageReceived;
                        OnEnded?.Invoke(this);
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

                    timeoutTimer.Change(_options.TurnTimer * 1000, Timeout.Infinite);
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