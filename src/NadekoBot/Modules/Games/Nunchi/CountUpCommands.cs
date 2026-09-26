#nullable disable
using NadekoBot.Modules.Games.Services;

namespace NadekoBot.Modules.Games;

public partial class Games
{
    [Group]
    public partial class CountUpCommands : NadekoModule<GamesService>
    {
        private readonly DiscordSocketClient _client;

        public CountUpCommands(DiscordSocketClient client)
            => _client = client;

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task CountUp()
        {
            var newGame = new CountUpGame(ctx.User.Id, ctx.User.ToString());
            CountUpGame countUp;

            //if a game was already active
            if ((countUp = _service.Games.GetOrAdd(ctx.Guild.Id, newGame)) != newGame)
            {
                // join it
                // if you failed joining, that means game is running or just ended
                if (!await countUp.Join(ctx.User.Id, ctx.User.ToString()))
                    return;

                await Response().Confirm(strs.countup_joined(countUp.ParticipantCount)).SendAsync();
                return;
            }


            try { await Response().Confirm(strs.countup_created).SendAsync(); }
            catch { }

            countUp.OnGameEnded += CountUpOnGameEnded;
            countUp.OnRoundEnded += CountUpOnRoundEnded;
            countUp.OnUserGuessed += CountUpOnUserGuessed;
            countUp.OnRoundStarted += CountUpOnRoundStarted;
            _client.MessageReceived += ClientMessageReceived;

            var success = await countUp.Initialize();
            if (!success)
            {
                _client.MessageReceived -= ClientMessageReceived;
                if (_service.Games.TryRemove(ctx.Guild.Id, out var game))
                    game.Dispose();
                await Response().Confirm(strs.countup_failed_to_start).SendAsync();
            }

            Task ClientMessageReceived(SocketMessage arg)
            {
                if (arg.Channel.Id != ctx.Channel.Id || !int.TryParse(arg.Content, out var number))
                    return Task.CompletedTask;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await countUp.Input(arg.Author.Id, arg.Author.ToString(), number);
                    }
                    catch
                    {
                    }
                });
                return Task.CompletedTask;
            }

            Task CountUpOnGameEnded(CountUpGame arg1, string arg2)
            {
                if (_service.Games.TryRemove(ctx.Guild.Id, out var game))
                {
                    _client.MessageReceived -= ClientMessageReceived;
                    game.Dispose();
                }

                if (arg2 is null)
                    return Response().Confirm(strs.countup_ended_no_winner).SendAsync();
                return Response().Confirm(strs.countup_ended(Format.Bold(arg2))).SendAsync();
            }
        }

        private Task CountUpOnRoundStarted(CountUpGame arg, int cur)
            => Response()
               .Confirm(strs.countup_round_started(Format.Bold(arg.ParticipantCount.ToString()),
                   Format.Bold(cur.ToString())))
               .SendAsync();

        private Task CountUpOnUserGuessed(CountUpGame arg)
            => Response()
                .Embed(CreateEmbed()
                    .WithOkColor()
                    .WithDescription(GetText(strs.countup_next_number(Format.Bold(arg.CurrentNumber.ToString()))))
                    .WithFooter($"{arg.PassedCount} / {arg.ParticipantCount}"))
                .SendAsync();

        private Task CountUpOnRoundEnded(CountUpGame arg1, (ulong Id, string Name)? arg2)
        {
            if (arg2.HasValue)
                return Response().Confirm(strs.countup_round_ended(Format.Bold(arg2.Value.Name))).SendAsync();
            return Response()
                   .Confirm(strs.countup_round_ended_boot(
                       Format.Bold("\n"
                                   + string.Join("\n, ",
                                       arg1.Participants.Select(x
                                           => x.Name)))))
                   .SendAsync(); // this won't work if there are too many users
        }

        private Task CountUpOnGameStarted(CountUpGame arg)
            => Response().Confirm(strs.countup_started(Format.Bold(arg.ParticipantCount.ToString()))).SendAsync();
    }
}