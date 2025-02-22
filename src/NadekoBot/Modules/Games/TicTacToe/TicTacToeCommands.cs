#nullable disable
using NadekoBot.Modules.Games.Common;
using NadekoBot.Modules.Games.Services;

namespace NadekoBot.Modules.Games;

public partial class Games
{
    [Group]
    public partial class TicTacToeCommands : NadekoModule<GamesService>
    {
        private readonly SemaphoreSlim _sem = new(1, 1);
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

            await _sem.WaitAsync(1000);
            try
            {
                if (_service.TicTacToeGames.TryGetValue(channel.Id, out var game))
                {
                    _ = Task.Run(async () =>
                    {
                        await game.Start((IGuildUser)ctx.User);
                    });
                    return;
                }

                game = new(Strings, _client, channel, (IGuildUser)ctx.User, options, _sender);
                _service.TicTacToeGames.Add(channel.Id, game);
                await Response().Confirm(strs.ttt_created(ctx.User)).SendAsync();

                game.OnEnded += _ =>
                {
                    _service.TicTacToeGames.Remove(channel.Id);
                    _sem.Dispose();
                };
            }
            finally
            {
                _sem.Release();
            }
        }
    }
}