using System.Net;
using System.Text;
using NadekoBot.Modules.Games.Common.Trivia;
using NadekoBot.Modules.Games.Services;

namespace NadekoBot.Modules.Games;

public partial class Games
{
    [Group]
    public partial class TriviaCommands : NadekoModule<TriviaGamesService>
    {
        private readonly ILocalDataCache _cache;
        private readonly ICurrencyService _cs;
        private readonly GamesConfigService _gamesConfig;
        private readonly DiscordSocketClient _client;

        public TriviaCommands(
            DiscordSocketClient client,
            ILocalDataCache cache,
            ICurrencyService cs,
            GamesConfigService gamesConfig)
        {
            _cache = cache;
            _cs = cs;
            _gamesConfig = gamesConfig;
            _client = client;
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [Priority(0)]
        [NadekoOptions<TriviaOptions>]
        public async Task Trivia(params string[] args)
        {
            var (opts, _) = OptionsParser.ParseFrom(new TriviaOptions(), args);

            var config = _gamesConfig.Data;
            if (opts.WinRequirement != 0
                && config.Trivia.MinimumWinReq > 0
                && config.Trivia.MinimumWinReq > opts.WinRequirement)
                return;

            var trivia = new TriviaGame(opts, _cache);
            if (_service.RunningTrivias.TryAdd(ctx.Guild.Id, trivia))
            {
                RegisterEvents(trivia);
                await trivia.RunAsync();
                return;
            }

            if (_service.RunningTrivias.TryGetValue(ctx.Guild.Id, out var tg))
            {
                await Response().Error(strs.trivia_already_running).SendAsync();
                await tg.TriggerQuestionAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task Tl()
        {
            if (_service.RunningTrivias.TryGetValue(ctx.Guild.Id, out var trivia))
            {
                await trivia.TriggerStatsAsync();
                return;
            }

            await Response().Error(strs.trivia_none).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task Tq()
        {
            var channel = (ITextChannel)ctx.Channel;

            if (_service.RunningTrivias.TryGetValue(channel.Guild.Id, out var trivia))
            {
                if (trivia.Stop())
                {
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

                return;
            }

            await Response().Error(strs.trivia_none).SendAsync();
        }

        private string GetLeaderboardString(TriviaGame tg)
        {
            var sb = new StringBuilder();

            foreach (var (id, pts) in tg.GetLeaderboard())
                sb.AppendLine(GetText(strs.trivia_points(Format.Bold($"<@{id}>"), pts)));

            return sb.ToString();
        }

        private EmbedBuilder? questionEmbed;
        private IUserMessage? questionMessage;
        private bool showHowToQuit;

        private void RegisterEvents(TriviaGame trivia)
        {
            trivia.OnQuestion += OnTriviaQuestion;
            trivia.OnHint += OnTriviaHint;
            trivia.OnGuess += OnTriviaGuess;
            trivia.OnEnded += OnTriviaEnded;
            trivia.OnStats += OnTriviaStats;
            trivia.OnTimeout += OnTriviaTimeout;
        }

        private void UnregisterEvents(TriviaGame trivia)
        {
            trivia.OnQuestion -= OnTriviaQuestion;
            trivia.OnHint -= OnTriviaHint;
            trivia.OnGuess -= OnTriviaGuess;
            trivia.OnEnded -= OnTriviaEnded;
            trivia.OnStats -= OnTriviaStats;
            trivia.OnTimeout -= OnTriviaTimeout;
        }

        private async Task OnTriviaHint(TriviaGame game, TriviaQuestion question)
        {
            try
            {
                if (questionMessage is null)
                {
                    game.Stop();
                    return;
                }

                if (questionEmbed is not null)
                    await questionMessage.ModifyAsync(m
                        => m.Embed = questionEmbed.WithFooter(question.GetHint()).Build());
            }
            catch (HttpException ex) when (ex.HttpCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
            {
                Log.Warning("Unable to edit message to show hint. Stopping trivia");
                game.Stop();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error editing trivia message");
            }
        }

        private async Task OnTriviaQuestion(TriviaGame game, TriviaQuestion question)
        {
            try
            {
                questionEmbed = CreateEmbed()
                                   .WithOkColor()
                                   .WithTitle(GetText(strs.trivia_game))
                                   .AddField(GetText(strs.category), question.Category)
                                   .AddField(GetText(strs.question), question.Question);

                showHowToQuit = !showHowToQuit;
                if (showHowToQuit)
                    questionEmbed.WithFooter(GetText(strs.trivia_quit($"{prefix}tq")));

                if (Uri.IsWellFormedUriString(question.ImageUrl, UriKind.Absolute))
                    questionEmbed.WithImageUrl(question.ImageUrl);

                questionMessage = await Response().Embed(questionEmbed).SendAsync();
            }
            catch (HttpException ex) when (ex.HttpCode is HttpStatusCode.NotFound
                                               or HttpStatusCode.Forbidden
                                               or HttpStatusCode.BadRequest)
            {
                Log.Warning("Unable to send trivia questions. Stopping immediately");
                game.Stop();
                throw;
            }
        }

        private async Task OnTriviaTimeout(TriviaGame _, TriviaQuestion question)
        {
            try
            {
                var embed = CreateEmbed()
                               .WithErrorColor()
                               .WithTitle(GetText(strs.trivia_game))
                               .WithDescription(GetText(strs.trivia_times_up(Format.Bold(question.Answer))));

                if (Uri.IsWellFormedUriString(question.AnswerImageUrl, UriKind.Absolute))
                    embed.WithImageUrl(question.AnswerImageUrl);

                await Response().Embed(embed).SendAsync();
            }
            catch
            {
                // ignored
            }
        }

        private async Task OnTriviaStats(TriviaGame game)
        {
            try
            {
                await Response().Confirm(GetText(strs.leaderboard), GetLeaderboardString(game)).SendAsync();
            }
            catch
            {
                // ignored
            }
        }

        private async Task OnTriviaEnded(TriviaGame game)
        {
            try
            {
                await Response().Embed(CreateEmbed()
                                    .WithOkColor()
                                    .WithAuthor(GetText(strs.trivia_ended))
                                    .WithTitle(GetText(strs.leaderboard))
                                    .WithDescription(GetLeaderboardString(game))).SendAsync();
            }
            catch
            {
                // ignored
            }
            finally
            {
                _service.RunningTrivias.TryRemove(ctx.Guild.Id, out _);
            }

            UnregisterEvents(game);
        }

        private async Task OnTriviaGuess(
            TriviaGame _,
            TriviaUser user,
            TriviaQuestion question,
            bool isWin)
        {
            try
            {
                var embed = CreateEmbed()
                               .WithOkColor()
                               .WithTitle(GetText(strs.trivia_game))
                               .WithDescription(GetText(strs.trivia_win(user.Name,
                                   Format.Bold(question.Answer))));

                if (Uri.IsWellFormedUriString(question.AnswerImageUrl, UriKind.Absolute))
                    embed.WithImageUrl(question.AnswerImageUrl);


                if (isWin)
                {
                    await Response().Embed(embed).SendAsync();

                    var reward = _gamesConfig.Data.Trivia.CurrencyReward;
                    if (reward > 0)
                        await _cs.AddAsync(user.Id, reward, new("trivia", "win"));

                    return;
                }

                embed.WithDescription(GetText(strs.trivia_guess(user.Name,
                    Format.Bold(question.Answer))));

                await Response().Embed(embed).SendAsync();
            }
            catch
            {
                // ignored
            }
        }
    }
}