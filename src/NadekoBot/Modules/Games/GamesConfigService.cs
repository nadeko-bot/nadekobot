#nullable disable
using NadekoBot.Common.Configs;
using NadekoBot.Modules.Games.Common;

namespace NadekoBot.Modules.Games.Services;

public sealed class GamesConfigService : ConfigServiceBase<GamesConfig>
{
    private const string FILE_PATH = "data/games.yml";
    private static readonly TypedKey<GamesConfig> _changeKey = new("config.games.updated");
    public override string Name { get; } = "games";

    public GamesConfigService(IConfigSeria serializer, IPubSub pubSub)
        : base(FILE_PATH, serializer, pubSub, _changeKey)
    {
        AddParsedProp("trivia.min_win_req",
            gs => gs.Trivia.MinimumWinReq,
            int.TryParse,
            ConfigPrinters.ToString,
            val => val > 0);
        AddParsedProp("trivia.currency_reward",
            gs => gs.Trivia.CurrencyReward,
            long.TryParse,
            ConfigPrinters.ToString,
            val => val >= 0);
        AddParsedProp("hangman.currency_reward",
            gs => gs.Hangman.CurrencyReward,
            long.TryParse,
            ConfigPrinters.ToString,
            val => val >= 0);

        Migrate();
    }

    private void Migrate()
    {
        if (data.Version < 1)
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

        if (data.Version < 5)
        {
            ModifyConfig(c =>
            {
                c.Version = 5;
            });
        }

        if (data.Version < 6)
        {
            ModifyConfig(c =>
            {
                c.Version = 6;
            });
        }
    }
}
