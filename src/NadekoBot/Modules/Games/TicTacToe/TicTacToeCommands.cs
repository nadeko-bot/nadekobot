#nullable disable
using NadekoBot.Modules.Games.Common;
using NadekoBot.Modules.Games.Services;

namespace NadekoBot.Modules.Games;

public partial class Games
{
    [Group]
    public partial class TicTacToeCommands : NadekoModule<GamesService>
    {
        private readonly DiscordSocketClient _client;

        public TicTacToeCommands(DiscordSocketClient client)
            => _client = client;

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [NadekoOptions<TicTacToe.Options>]
        public async Task TicTacToe(params string[] args)
        {
            var (options, _) = OptionsParser.ParseFrom(new TicTacToe.Options(), args);
            var channel = (ITextChannel)ctx.Channel;

            if (_service.TicTacToeGames.TryGetValue(channel.Id, out var existingGame))
            {
                _ = Task.Run(async () =>
                {
                    await existingGame.Start((IGuildUser)ctx.User);
                });
                return;
            }

            var game = new TicTacToe(Strings, _client, channel, (IGuildUser)ctx.User, options, _sender);
            if (!_service.TicTacToeGames.TryAdd(channel.Id, game))
                return;

            await Response().Confirm(strs.ttt_created(ctx.User)).SendAsync();

            game.OnEnded += _ =>
            {
                _service.TicTacToeGames.TryRemove(channel.Id, out _);
            };
        }
    }
}