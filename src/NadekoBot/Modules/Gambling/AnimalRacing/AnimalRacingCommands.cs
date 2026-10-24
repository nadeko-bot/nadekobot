#nullable disable
using NadekoBot.Common.TypeReaders;
using NadekoBot.Modules.Gambling.Common;
using NadekoBot.Modules.Gambling.Common.AnimalRacing;
using NadekoBot.Modules.Gambling.Common.AnimalRacing.Exceptions;
using NadekoBot.Modules.Gambling.Services;
using NadekoBot.Modules.Games.Quests;
using NadekoBot.Modules.Games.Services;

namespace NadekoBot.Modules.Gambling;

// wth is this, needs full rewrite
public partial class Gambling
{
    [Group]
    public partial class AnimalRacingCommands : GamblingModule<AnimalRaceService>
    {
        private readonly ICurrencyService _cs;
        private readonly DiscordSocketClient _client;
        private readonly GamesConfigService _gamesConf;
        private readonly QuestService _quests;

        private IUserMessage raceMessage;

        public AnimalRacingCommands(
            ICurrencyService cs,
            DiscordSocketClient client,
            GamblingConfigService gamblingConf,
            GamesConfigService gamesConf,
            QuestService quests)
            : base(gamblingConf)
        {
            _cs = cs;
            _client = client;
            _gamesConf = gamesConf;
            _quests = quests;
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [NadekoOptions<RaceOptions>]
        public Task Race(params string[] args)
        {
            var (options, _) = OptionsParser.ParseFrom(new RaceOptions(), args);

            // a race without animals never starts and so never ends
            if (_gamesConf.Data.RaceAnimals.Count == 0)
                return Response().Error(strs.error_occured).SendAsync();

            var ar = new AnimalRace(options, _cs, _gamesConf.Data.RaceAnimals.Shuffle(), _quests);
            if (!_service.AnimalRaces.TryAdd(ctx.Guild.Id, ar))
                return Response()
                    .Error(GetText(strs.animal_race), GetText(strs.animal_race_already_started))
                    .SendAsync();

            var count = 0;
            var channelId = ctx.Channel.Id;

            Task ClientMessageReceived(SocketMessage arg)
            {
                // every 9 messages the race message is sent again, so it stays visible
                if (arg.Channel.Id == channelId
                    && ar.CurrentPhase == AnimalRace.Phase.Running
                    && Interlocked.Increment(ref count) % 9 == 0)
                    raceMessage = null;

                return Task.CompletedTask;
            }

            void EndRace()
            {
                _client.MessageReceived -= ClientMessageReceived;
                _service.AnimalRaces.TryRemove(ctx.Guild.Id, out _);
            }

            Task ArOnStartingFailed(AnimalRace race)
            {
                EndRace();
                race.Dispose();
                return Response().Error(strs.animal_race_failed).SendAsync();
            }

            Task ArOnEnded(AnimalRace race)
            {
                EndRace();
                var failed = race.Failed || race.FinishedUsers.Count == 0;
                var winner = failed ? null : race.FinishedUsers[0];
                var multi = race.Multi;
                ar.Dispose();
                if (winner is null)
                    return Response().Error(strs.error_occured).SendAsync();

                if (winner.Bet > 0)
                {
                    return Response()
                        .Embed(CreateEmbed()
                            .WithOkColor()
                            .WithTitle(GetText(strs.animal_race))
                            .WithDescription(GetText(strs.animal_race_won_money(
                                Format.Bold(winner.Username),
                                winner.Animal.Icon,
                                N(winner.Bet * multi))))
                            .WithFooter($"x{multi:F2}"))
                        .SendAsync();
                }

                return Response()
                    .Confirm(GetText(strs.animal_race),
                        GetText(strs.animal_race_won(Format.Bold(winner.Username), winner.Animal.Icon)))
                    .SendAsync();
            }

            ar.OnStartingFailed += ArOnStartingFailed;
            ar.OnStateUpdate += Ar_OnStateUpdate;
            ar.OnEnded += ArOnEnded;
            ar.OnStarted += Ar_OnStarted;
            _client.MessageReceived += ClientMessageReceived;
            ar.Initialize();

            return Response()
                .Confirm(GetText(strs.animal_race),
                    GetText(strs.animal_race_starting(options.StartTime)),
                    footer: GetText(strs.animal_race_join_instr(prefix)))
                .SendAsync();
        }

        private Task Ar_OnStarted(AnimalRace race)
        {
            if (race.Users.Count == race.MaxUsers)
                return Response().Confirm(GetText(strs.animal_race), GetText(strs.animal_race_full)).SendAsync();
            return Response()
                .Confirm(GetText(strs.animal_race),
                    GetText(strs.animal_race_starting_with_x(race.Users.Count)))
                .SendAsync();
        }

        private async Task Ar_OnStateUpdate(AnimalRace race)
        {
            var text = $@"|🏁 🏁 🏁 🏁 🏁 🏁 🏁 🏁 🏁 🏁 🏁 🏁 🏁 🏁 🏁🔚|
{string.Join("\n", race.Users.Select(p =>
{
    var index = race.FinishedUsers.IndexOf(p);
    var extra = index == -1 ? "" : $"#{index + 1} {(index == 0 ? "🏆" : "")}";
    return $"{(int)(p.Progress / 60f * 100),-2}%|{new string('‣', p.Progress) + p.Animal.Icon + extra}";
}))}
|🏁 🏁 🏁 🏁 🏁 🏁 🏁 🏁 🏁 🏁 🏁 🏁 🏁 🏁 🏁 🔚|";

            var msg = raceMessage;

            if (msg is null)
                raceMessage = await Response().Confirm(text).SendAsync();
            else
            {
                await msg.ModifyAsync(x => x.Embed = CreateEmbed()
                    .WithTitle(GetText(strs.animal_race))
                    .WithDescription(text)
                    .WithOkColor()
                    .Build());
            }
        }


        [Cmd]
        [SpendsCurrency]
        [RequireContext(ContextType.Guild)]
        public async Task JoinRace([OverrideTypeReader(typeof(BalanceTypeReader))] long amount = default)
        {
            if (!await CheckBetOptional(amount))
                return;

            if (!_service.AnimalRaces.TryGetValue(ctx.Guild.Id, out var ar))
            {
                await Response().Error(strs.race_not_exist).SendAsync();
                return;
            }

            try
            {
                var user = await ar.JoinRace(ctx.User.Id, ctx.User.ToString(), amount);
                if (amount > 0)
                {
                    await Response()
                        .Confirm(GetText(strs.animal_race_join_bet(ctx.User.Mention,
                            user.Animal.Icon,
                            N(amount))))
                        .SendAsync();
                }
                else
                    await Response()
                        .Confirm(strs.animal_race_join(ctx.User.Mention, user.Animal.Icon))
                        .SendAsync();
            }
            catch (ArgumentOutOfRangeException)
            {
                //ignore if user inputed an invalid amount
            }
            catch (AlreadyJoinedException)
            {
                await Response().Error(strs.animal_race_already_joined).SendAsync();
            }
            catch (AlreadyStartedException)
            {
                await Response().Error(strs.animal_race_already_started).SendAsync();
            }
            catch (AnimalRaceFullException)
            {
                await Response().Confirm(GetText(strs.animal_race), GetText(strs.animal_race_full)).SendAsync();
            }
            catch (NotEnoughFundsException)
            {
                await Response().Error(GetText(strs.not_enough(CurrencySign))).SendAsync();
            }
        }
    }
}