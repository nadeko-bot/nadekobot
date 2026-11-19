using NadekoBot.Common.TypeReaders;
using NadekoBot.Modules.Gambling.Common;
using NadekoBot.Modules.Gambling.Common.AnimalRacing;
using NadekoBot.Modules.Gambling.Services;

namespace NadekoBot.Modules.Gambling;

public partial class Gambling
{
    [Group]
    public partial class AnimalRacingCommands(GamblingConfigService gamblingConf)
        : GamblingModule<AnimalRaceService>(gamblingConf)
    {
        [Cmd]
        [RequireContext(ContextType.Guild)]
        [NadekoOptions<RaceOptions>]
        public async Task Race(params string[] args)
        {
            var (options, _) = OptionsParser.ParseFrom(new RaceOptions(), args);

            var result = await _service.StartAsync((ITextChannel)ctx.Channel, options);
            switch (result)
            {
                case RaceStartResult.Started:
                    await Response()
                        .Confirm(GetText(strs.animal_race),
                            GetText(strs.animal_race_starting(options.StartTime)),
                            footer: GetText(strs.animal_race_join_instr(prefix)))
                        .SendAsync();
                    break;
                case RaceStartResult.NoAnimals:
                    await Response().Error(strs.error_occured).SendAsync();
                    break;
                case RaceStartResult.AlreadyRunning:
                    await Response()
                        .Error(GetText(strs.animal_race), GetText(strs.animal_race_already_started))
                        .SendAsync();
                    break;
            }
        }

        [Cmd]
        [SpendsCurrency]
        [RequireContext(ContextType.Guild)]
        public async Task JoinRace([OverrideTypeReader(typeof(BalanceTypeReader))] long amount = default)
        {
            if (amount < 0 || !await CheckBetOptional(amount))
                return;

            var (result, animal) = await _service.JoinAsync((IGuildUser)ctx.User, amount);
            switch (result)
            {
                case RaceJoinResult.Joined when animal is not null:
                    if (amount > 0)
                    {
                        await Response()
                            .Confirm(strs.animal_race_join_bet(ctx.User.Mention, animal.Icon, N(amount)))
                            .SendAsync();
                    }
                    else
                    {
                        await Response()
                            .Confirm(strs.animal_race_join(ctx.User.Mention, animal.Icon))
                            .SendAsync();
                    }

                    break;
                case RaceJoinResult.NoRace:
                    await Response().Error(strs.race_not_exist).SendAsync();
                    break;
                case RaceJoinResult.AlreadyJoined:
                    await Response().Error(strs.animal_race_already_joined).SendAsync();
                    break;
                case RaceJoinResult.AlreadyStarted:
                    await Response().Error(strs.animal_race_already_started).SendAsync();
                    break;
                case RaceJoinResult.Full:
                    await Response().Confirm(GetText(strs.animal_race), GetText(strs.animal_race_full)).SendAsync();
                    break;
                case RaceJoinResult.NotEnough:
                    await Response().Error(strs.not_enough(CurrencySign)).SendAsync();
                    break;
            }
        }
    }
}
