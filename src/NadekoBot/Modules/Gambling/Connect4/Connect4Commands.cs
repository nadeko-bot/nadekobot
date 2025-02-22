#nullable disable
using NadekoBot.Modules.Gambling.Common;
using NadekoBot.Modules.Gambling.Common.Connect4;
using NadekoBot.Modules.Gambling.Services;
using System.Text;

namespace NadekoBot.Modules.Gambling;

public partial class Gambling
{
    [Group]
    public partial class Connect4Commands : GamblingModule<GamblingService>
    {
        private static readonly string[] _numbers =
        [
            ":one:", ":two:", ":three:", ":four:", ":five:", ":six:", ":seven:", ":eight:"
        ];

        private int RepostCounter
        {
            get => repostCounter;
            set
            {
                if (value is < 0 or > 7)
                    repostCounter = 0;
                else
                    repostCounter = value;
            }
        }

        private readonly DiscordSocketClient _client;

        private IUserMessage msg;

        private int repostCounter;

        public Connect4Commands(DiscordSocketClient client, GamblingConfigService gamb)
            : base(gamb)
        {
            _client = client;
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [NadekoOptions<Connect4Game.Options>]
        public async Task Connect4(params string[] args)
        {
            var (options, _) = OptionsParser.ParseFrom(new Connect4Game.Options(), args);

            var newGame = new Connect4Game(ctx.User.Id, ctx.User.ToString(), options);
            Connect4Game game;
            if ((game = _service.Connect4Games.GetOrAdd(ctx.Channel.Id, newGame)) != newGame)
            {
                if (game.CurrentPhase != Connect4Game.Phase.Joining)
                    return;

                newGame.Dispose();
                //means game already exists, try to join
                await game.Join(ctx.User.Id, ctx.User.ToString());
                return;
            }

            game.OnGameStateUpdated += Game_OnGameStateUpdated;
            game.OnGameFailedToStart += GameOnGameFailedToStart;
            game.OnGameEnded += GameOnGameEnded;
            _client.MessageReceived += ClientMessageReceived;

            game.Initialize();
            await Response().Confirm(strs.connect4_created).SendAsync();

            Task ClientMessageReceived(SocketMessage arg)
            {
                if (ctx.Channel.Id != arg.Channel.Id)
                    return Task.CompletedTask;

                _ = Task.Run(async () =>
                {
                    var success = false;
                    if (int.TryParse(arg.Content, out var col))
                        success = await game.Input(arg.Author.Id, col);

                    if (success)
                    {
                        try { await arg.DeleteAsync(); }
                        catch { }
                    }
                    else
                    {
                        if (game.CurrentPhase is Connect4Game.Phase.Joining or Connect4Game.Phase.Ended)
                            return;
                        RepostCounter++;
                        if (RepostCounter == 0)
                        {
                            try { msg = await Response().Embed(msg.Embeds.First().ToEmbedBuilder()).SendAsync(); }
                            catch { }
                        }
                    }
                });
                return Task.CompletedTask;
            }

            Task GameOnGameFailedToStart(Connect4Game arg)
            {
                if (_service.Connect4Games.TryRemove(ctx.Channel.Id, out var toDispose))
                {
                    _client.MessageReceived -= ClientMessageReceived;
                    toDispose.Dispose();
                }

                return Response().Error(strs.connect4_failed_to_start).SendAsync();
            }

            Task GameOnGameEnded(Connect4Game arg, Connect4Game.Result result)
            {
                if (_service.Connect4Games.TryRemove(ctx.Channel.Id, out var toDispose))
                {
                    _client.MessageReceived -= ClientMessageReceived;
                    toDispose.Dispose();
                }

                string title;
                if (result == Connect4Game.Result.CurrentPlayerWon)
                {
                    title = GetText(strs.connect4_won(Format.Bold(arg.CurrentPlayer.Username),
                        Format.Bold(arg.OtherPlayer.Username)));
                }
                else if (result == Connect4Game.Result.OtherPlayerWon)
                {
                    title = GetText(strs.connect4_won(Format.Bold(arg.OtherPlayer.Username),
                        Format.Bold(arg.CurrentPlayer.Username)));
                }
                else
                    title = GetText(strs.connect4_draw);

                return msg.ModifyAsync(x => x.Embed = CreateEmbed()
                                                             .WithTitle(title)
                                                             .WithDescription(GetGameStateText(game))
                                                             .WithOkColor()
                                                             .Build());
            }
        }

        private async Task Game_OnGameStateUpdated(Connect4Game game)
        {
            var embed = CreateEmbed()
                               .WithTitle($"{game.CurrentPlayer.Username} vs {game.OtherPlayer.Username}")
                               .WithDescription(GetGameStateText(game))
                               .WithOkColor();


            if (msg is null)
                msg = await Response().Embed(embed).SendAsync();
            else
                await msg.ModifyAsync(x => x.Embed = embed.Build());
        }

        private string GetGameStateText(Connect4Game game)
        {
            var sb = new StringBuilder();

            if (game.CurrentPhase is Connect4Game.Phase.P1Move or Connect4Game.Phase.P2Move)
                sb.AppendLine(GetText(strs.connect4_player_to_move(Format.Bold(game.CurrentPlayer.Username))));

            for (var i = Connect4Game.NUMBER_OF_ROWS; i > 0; i--)
            {
                for (var j = 0; j < Connect4Game.NUMBER_OF_COLUMNS; j++)
                {
                    var cur = game.GameState[i + (j * Connect4Game.NUMBER_OF_ROWS) - 1];

                    if (cur == Connect4Game.Field.Empty)
                        sb.Append("⚫"); //black circle
                    else if (cur == Connect4Game.Field.P1)
                        sb.Append("🔴"); //red circle
                    else
                        sb.Append("🔵"); //blue circle
                }

                sb.AppendLine();
            }

            for (var i = 0; i < Connect4Game.NUMBER_OF_COLUMNS; i++)
                sb.Append(_numbers[i]);

            return sb.ToString();
        }
    }
}