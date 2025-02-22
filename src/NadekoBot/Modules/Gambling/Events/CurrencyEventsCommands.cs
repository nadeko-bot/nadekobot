#nullable disable
using NadekoBot.Modules.Gambling.Common;
using NadekoBot.Modules.Gambling.Common.Events;
using NadekoBot.Modules.Gambling.Services;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Gambling;

public partial class Gambling
{
    [Group]
    public partial class CurrencyEventsCommands : GamblingModule<CurrencyEventsService>
    {
        public CurrencyEventsCommands(GamblingConfigService gamblingConf)
            : base(gamblingConf)
        {
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [NadekoOptions<EventOptions>]
        [OwnerOnly]
        public async Task EventStart(CurrencyEvent.Type ev, params string[] options)
        {
            var (opts, _) = OptionsParser.ParseFrom(new EventOptions(), options);
            if (!await _service.TryCreateEventAsync(ctx.Guild.Id, ctx.Channel.Id, ev, opts, GetEmbed))
                await Response().Error(strs.start_event_fail).SendAsync();
        }

        private EmbedBuilder GetEmbed(CurrencyEvent.Type type, EventOptions opts, long currentPot)
            => type switch
            {
                CurrencyEvent.Type.Reaction => CreateEmbed()
                                                  .WithOkColor()
                                                  .WithTitle(GetText(strs.event_title(type.ToString())))
                                                  .WithDescription(GetReactionDescription(opts.Amount, currentPot))
                                                  .WithFooter(GetText(strs.event_duration_footer(opts.Hours))),
                CurrencyEvent.Type.GameStatus => CreateEmbed()
                                                    .WithOkColor()
                                                    .WithTitle(GetText(strs.event_title(type.ToString())))
                                                    .WithDescription(GetGameStatusDescription(opts.Amount, currentPot))
                                                    .WithFooter(GetText(strs.event_duration_footer(opts.Hours))),
                _ => throw new ArgumentOutOfRangeException(nameof(type))
            };

        private string GetReactionDescription(long amount, long potSize)
        {
            var potSizeStr = Format.Bold(potSize == 0 ? "∞" + CurrencySign : N(potSize));

            return GetText(strs.new_reaction_event(CurrencySign, Format.Bold(N(amount)), potSizeStr));
        }

        private string GetGameStatusDescription(long amount, long potSize)
        {
            var potSizeStr = Format.Bold(potSize == 0 ? "∞" + CurrencySign : potSize + CurrencySign);

            return GetText(strs.new_gamestatus_event(CurrencySign, Format.Bold(N(amount)), potSizeStr));
        }
    }
}