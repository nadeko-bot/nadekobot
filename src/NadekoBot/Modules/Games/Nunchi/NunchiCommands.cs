#nullable disable
using NadekoBot.Modules.Games.Common.Nunchi;
using NadekoBot.Modules.Games.Services;

namespace NadekoBot.Modules.Games;

public partial class Games
{
    [Group]
    public partial class NunchiCommands : NadekoModule<GamesService>
    {
        private readonly DiscordSocketClient _client;

        public NunchiCommands(DiscordSocketClient client)
            => _client = client;

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task Nunchi()
        {
            var newNunchi = new NunchiGame(ctx.User.Id, ctx.User.ToString());
            NunchiGame nunchi;

            //if a game was already active
            if ((nunchi = _service.NunchiGames.GetOrAdd(ctx.Guild.Id, newNunchi)) != newNunchi)
            {
                // join it
                // if you failed joining, that means game is running or just ended
                if (!await nunchi.Join(ctx.User.Id, ctx.User.ToString()))
                    return;

                await Response().Confirm(strs.nunchi_joined(nunchi.ParticipantCount)).SendAsync();
                return;
            }


            try { await Response().Confirm(strs.nunchi_created).SendAsync(); }
            catch { }

            nunchi.OnGameEnded += NunchiOnGameEnded;
            //nunchi.OnGameStarted += Nunchi_OnGameStarted;
            nunchi.OnRoundEnded += Nunchi_OnRoundEnded;
            nunchi.OnUserGuessed += Nunchi_OnUserGuessed;
            nunchi.OnRoundStarted += Nunchi_OnRoundStarted;
            _client.MessageReceived += ClientMessageReceived;

            var success = await nunchi.Initialize();
            if (!success)
            {
                if (_service.NunchiGames.TryRemove(ctx.Guild.Id, out var game))
                    game.Dispose();
                await Response().Confirm(strs.nunchi_failed_to_start).SendAsync();
            }

            Task ClientMessageReceived(SocketMessage arg)
            {
                _ = Task.Run(async () =>
                {
                    if (arg.Channel.Id != ctx.Channel.Id)
                        return;

                    if (!int.TryParse(arg.Content, out var number))
                        return;
                    try
                    {
                        await nunchi.Input(arg.Author.Id, arg.Author.ToString(), number);
                    }
                    catch
                    {
                    }
                });
                return Task.CompletedTask;
            }

            Task NunchiOnGameEnded(NunchiGame arg1, string arg2)
            {
                if (_service.NunchiGames.TryRemove(ctx.Guild.Id, out var game))
                {
                    _client.MessageReceived -= ClientMessageReceived;
                    game.Dispose();
                }

                if (arg2 is null)
                    return Response().Confirm(strs.nunchi_ended_no_winner).SendAsync();
                return Response().Confirm(strs.nunchi_ended(Format.Bold(arg2))).SendAsync();
            }
        }

        private Task Nunchi_OnRoundStarted(NunchiGame arg, int cur)
            => Response()
               .Confirm(strs.nunchi_round_started(Format.Bold(arg.ParticipantCount.ToString()),
                   Format.Bold(cur.ToString())))
               .SendAsync();

        private Task Nunchi_OnUserGuessed(NunchiGame arg)
            => Response().Confirm(strs.nunchi_next_number(Format.Bold(arg.CurrentNumber.ToString()))).SendAsync();

        private Task Nunchi_OnRoundEnded(NunchiGame arg1, (ulong Id, string Name)? arg2)
        {
            if (arg2.HasValue)
                return Response().Confirm(strs.nunchi_round_ended(Format.Bold(arg2.Value.Name))).SendAsync();
            return Response()
                   .Confirm(strs.nunchi_round_ended_boot(
                       Format.Bold("\n"
                                   + string.Join("\n, ",
                                       arg1.Participants.Select(x
                                           => x.Name)))))
                   .SendAsync(); // this won't work if there are too many users
        }

        private Task Nunchi_OnGameStarted(NunchiGame arg)
            => Response().Confirm(strs.nunchi_started(Format.Bold(arg.ParticipantCount.ToString()))).SendAsync();
    }
}