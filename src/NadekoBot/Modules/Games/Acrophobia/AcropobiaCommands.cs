#nullable disable
using NadekoBot.Modules.Games.Common.Acrophobia;
using NadekoBot.Modules.Games.Services;
using System.Collections.Immutable;

namespace NadekoBot.Modules.Games;

public partial class Games
{
    [Group]
    public partial class AcropobiaCommands : NadekoModule<GamesService>
    {
        private readonly DiscordSocketClient _client;

        public AcropobiaCommands(DiscordSocketClient client)
            => _client = client;

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [NadekoOptions<AcrophobiaGame.Options>]
        public async Task Acrophobia(params string[] args)
        {
            var (options, _) = OptionsParser.ParseFrom(new AcrophobiaGame.Options(), args);
            var channel = (ITextChannel)ctx.Channel;

            var game = new AcrophobiaGame(options);
            if (_service.AcrophobiaGames.TryAdd(channel.Id, game))
            {
                try
                {
                    game.OnStarted += Game_OnStarted;
                    game.OnEnded += Game_OnEnded;
                    game.OnVotingStarted += Game_OnVotingStarted;
                    game.OnUserVoted += Game_OnUserVoted;
                    _client.MessageReceived += ClientMessageReceived;
                    await game.Run();
                }
                finally
                {
                    _client.MessageReceived -= ClientMessageReceived;
                    _service.AcrophobiaGames.TryRemove(channel.Id, out game);
                    game?.Dispose();
                }
            }
            else
                await Response().Error(strs.acro_running).SendAsync();

            Task ClientMessageReceived(SocketMessage msg)
            {
                if (msg.Channel.Id != ctx.Channel.Id)
                    return Task.CompletedTask;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var success = await game.UserInput(msg.Author.Id, msg.Author.ToString(), msg.Content);
                        if (success)
                            await msg.DeleteAsync();
                    }
                    catch { }
                });

                return Task.CompletedTask;
            }
        }

        private Task Game_OnStarted(AcrophobiaGame game)
        {
            var embed = CreateEmbed()
                           .WithOkColor()
                           .WithTitle(GetText(strs.acrophobia))
                           .WithDescription(
                               GetText(strs.acro_started(Format.Bold(string.Join(".", game.StartingLetters)))))
                           .WithFooter(GetText(strs.acro_started_footer(game.Opts.SubmissionTime)));

            return Response().Embed(embed).SendAsync();
        }

        private Task Game_OnUserVoted(string user)
            => Response().Confirm(GetText(strs.acrophobia), GetText(strs.acro_vote_cast(Format.Bold(user)))).SendAsync();

        private async Task Game_OnVotingStarted(
            AcrophobiaGame game,
            ImmutableArray<KeyValuePair<AcrophobiaUser, int>> submissions)
        {
            if (submissions.Length == 0)
            {
                await Response().Error(GetText(strs.acrophobia), GetText(strs.acro_ended_no_sub)).SendAsync();
                return;
            }

            if (submissions.Length == 1)
            {
                await Response().Embed(CreateEmbed()
                                                .WithOkColor()
                                                .WithDescription(GetText(
                                                    strs.acro_winner_only(
                                                        Format.Bold(submissions.First().Key.UserName))))
                                                .WithFooter(submissions.First().Key.Input)).SendAsync();
                return;
            }


            var i = 0;
            var embed = CreateEmbed()
                           .WithOkColor()
                           .WithTitle(GetText(strs.acrophobia) + " - " + GetText(strs.submissions_closed))
                           .WithDescription(GetText(strs.acro_nym_was(
                               Format.Bold(string.Join(".", game.StartingLetters))
                               + "\n"
                               + $@"--
{submissions.Aggregate("", (agg, cur) => agg + $"`{++i}.` **{cur.Key.Input}**\n")}
--")))
                           .WithFooter(GetText(strs.acro_vote));

            await Response().Embed(embed).SendAsync();
        }

        private async Task Game_OnEnded(AcrophobiaGame game, ImmutableArray<KeyValuePair<AcrophobiaUser, int>> votes)
        {
            if (!votes.Any() || votes.All(x => x.Value == 0))
            {
                await Response().Error(GetText(strs.acrophobia), GetText(strs.acro_no_votes_cast)).SendAsync();
                return;
            }

            var table = votes.OrderByDescending(v => v.Value);
            var winner = table.First();
            var embed = CreateEmbed()
                           .WithOkColor()
                           .WithTitle(GetText(strs.acrophobia))
                           .WithDescription(GetText(strs.acro_winner(Format.Bold(winner.Key.UserName),
                               Format.Bold(winner.Value.ToString()))))
                           .WithFooter(winner.Key.Input);

            await Response().Embed(embed).SendAsync();
        }
    }
}