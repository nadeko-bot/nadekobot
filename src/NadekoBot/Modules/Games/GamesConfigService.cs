using NadekoBot.Common.Configs;
using NadekoBot.Modules.Games.Common;

namespace NadekoBot.Modules.Games.Services;

public sealed class GamesConfigService : ConfigServiceBase<GamesConfig>
{
    private const string FILE_PATH = "data/games.yml";
    private static readonly TypedKey<GamesConfig> _changeKey = new("config.games.updated");
    public override string Name { get; } = "games";

    public GamesConfigService(IConfigSeria serializer, IPubSub pubSub, BotPolicy policy)
        : base(FILE_PATH, serializer, pubSub, _changeKey, policy)
    {
        AddNumberProp("trivia.min_win_req",
            static gs => gs.Trivia.MinimumWinReq,
            static (gs, v) => gs.Trivia.MinimumWinReq = v,
            int.TryParse,
            ConfigPrinters.ToString,
            "Users won't be able to start trivia games which have a smaller win requirement than this",
            min: 1,
            max: null);
        AddNumberProp("trivia.currency_reward",
            static gs => gs.Trivia.CurrencyReward,
            static (gs, v) => gs.Trivia.CurrencyReward = v,
            long.TryParse,
            ConfigPrinters.ToString,
            "The amount of currency awarded to the winner of the trivia game",
            min: 0,
            max: null, strict: true);
        AddNumberProp("hangman.currency_reward",
            static gs => gs.Hangman.CurrencyReward,
            static (gs, v) => gs.Hangman.CurrencyReward = v,
            long.TryParse,
            ConfigPrinters.ToString,
            "The amount of currency awarded to the winner of a hangman game",
            min: 0,
            max: null, strict: true);

        Migrate();
    }

    private void Migrate()
    {
        if (Data.Version < 1)
        {
            ModifyConfig(c =>
            {
                c.Version = 1;
                c.Hangman = new()
                {
                    CurrencyReward = 0
                };
            });
        }

        if (Data.Version < 5)
        {
            ModifyConfig(c =>
            {
                c.Version = 5;
            });
        }

        if (Data.Version < 6)
        {
            ModifyConfig(c =>
            {
                c.Version = 6;
            });
        }
    }
}
