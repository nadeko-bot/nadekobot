using NadekoBot.Modules.Games.Common.Trivia;
using NadekoBot.Modules.Games.Services;

namespace NadekoBot.Modules.Games;

public partial class Games
{
    [Group]
    public partial class TriviaCommands(GamesConfigService gamesConfig) : NadekoModule<TriviaGamesService>
    {
        [Cmd]
        [RequireContext(ContextType.Guild)]
        [Priority(0)]
        [NadekoOptions<TriviaOptions>]
        public async Task Trivia(params string[] args)
        {
            if (await RefuseWithoutMessageContentAsync("trivia"))
                return;

            var (opts, _) = OptionsParser.ParseFrom(new TriviaOptions(), args);

            var config = gamesConfig.Data;
            if (opts.WinRequirement != 0
                && config.Trivia.MinimumWinReq > 0
                && config.Trivia.MinimumWinReq > opts.WinRequirement)
            {
                await Response().Error(strs.trivia_win_req_too_low(config.Trivia.MinimumWinReq)).SendAsync();
                return;
            }

            if (await _service.StartAsync((ITextChannel)ctx.Channel, opts))
                return;

            await Response().Error(strs.trivia_already_running).SendAsync();
            await _service.RepostQuestionAsync(ctx.Guild.Id);
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task Tl()
        {
            if (await _service.ShowLeaderboardAsync(ctx.Guild.Id))
                return;

            await Response().Error(strs.trivia_none).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task Tq()
        {
            var result = await _service.StopAsync(ctx.Guild.Id);
            if (result == TriviaStopResult.NotRunning)
            {
                await Response().Error(strs.trivia_none).SendAsync();
                return;
            }

            if (result != TriviaStopResult.Stopping)
                return;

            try
            {
                await Response()
                    .Confirm(GetText(strs.trivia_game), GetText(strs.trivia_stopping))
                    .SendAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error sending trivia stopping message");
            }
        }
    }
}
