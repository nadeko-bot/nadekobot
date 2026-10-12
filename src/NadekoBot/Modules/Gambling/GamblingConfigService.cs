using NadekoBot.Common.Configs;
using NadekoBot.Modules.Gambling.Common;

namespace NadekoBot.Modules.Gambling.Services;

public sealed class GamblingConfigService : ConfigServiceBase<GamblingConfig>
{
    private const string FILE_PATH = "data/gambling.yml";
    private static readonly TypedKey<GamblingConfig> _changeKey = new("config.gambling.updated");

    public override string Name
        => "gambling";

    public const int MAX_CURRENCY_NAME_LENGTH = 32;

    // Long enough for an animated custom emoji.
    public const int MAX_CURRENCY_SIGN_LENGTH = 64;

    // Generation picks a random amount up to max + 1, which must not overflow.
    public const int MAX_GEN_AMOUNT = 1_000_000_000;

    public GamblingConfigService(IConfigSeria serializer, IPubSub pubSub, BotPolicy policy)
        : base(FILE_PATH, serializer, pubSub, _changeKey, policy)
    {
        AddTextProp("currency.name",
            static gs => gs.Currency.Name,
            static (gs, v) => gs.Currency.Name = v,
            MAX_CURRENCY_NAME_LENGTH,
            "What is the name of the currency",
            required: true);

        AddTextProp("currency.sign",
            static gs => gs.Currency.Sign,
            static (gs, v) => gs.Currency.Sign = v,
            MAX_CURRENCY_SIGN_LENGTH,
            "What is the emoji/character which represents the currency",
            required: true);

        AddNumberProp("minbet",
            static gs => gs.MinBet,
            static (gs, v) => gs.MinBet = v,
            int.TryParse,
            ConfigPrinters.ToString,
            "Minimum amount users can bet (>=0)",
            min: 0,
            max: null, strict: true);

        AddNumberProp("maxbet",
            static gs => gs.MaxBet,
            static (gs, v) => gs.MaxBet = v,
            int.TryParse,
            ConfigPrinters.ToString,
            "Maximum amount users can bet. Set 0 for unlimited",
            min: 0,
            max: null, strict: true);

        AddNumberProp("gen.min",
            static gs => gs.Generation.MinAmount,
            static (gs, v) => gs.Generation.MinAmount = v,
            int.TryParse,
            ConfigPrinters.ToString,
            "Minimum amount of currency that can spawn",
            min: 1,
            max: MAX_GEN_AMOUNT, strict: true);

        AddNumberProp("gen.max",
            static gs => gs.Generation.MaxAmount,
            static (gs, v) => gs.Generation.MaxAmount = v,
            int.TryParse,
            ConfigPrinters.ToString,
            "Maximum amount of currency that can spawn",
            min: 1,
            max: MAX_GEN_AMOUNT, strict: true);

        AddNumberProp("gen.cd",
            static gs => gs.Generation.GenCooldown,
            static (gs, v) => gs.Generation.GenCooldown = v,
            int.TryParse,
            ConfigPrinters.ToString,
            "How many seconds have to pass for the next message to have a chance to spawn currency",
            min: 0,
            max: null, strict: true);

        AddNumberProp("gen.chance",
            static gs => gs.Generation.Chance,
            static (gs, v) => gs.Generation.Chance = v,
            decimal.TryParse,
            ConfigPrinters.ToString,
            "Every message sent has a certain % chance to generate the currency",
            min: 0,
            max: 1, strict: true);

        AddParsedProp("gen.has_pw",
            static gs => gs.Generation.HasPassword,
            static (gs, v) => gs.Generation.HasPassword = v,
            bool.TryParse,
            ConfigPrinters.ToString,
            "When currency is generated, should it also have a random password");

        AddNumberProp("bf.multi",
            static gs => gs.BetFlip.Multiplier,
            static (gs, v) => gs.BetFlip.Multiplier = v,
            decimal.TryParse,
            ConfigPrinters.ToString,
            "Bet multiplier if user guesses correctly",
            min: 1,
            max: null);

        AddNumberProp("decay.percent",
            static gs => gs.Decay.Percent,
            static (gs, v) => gs.Decay.Percent = v,
            decimal.TryParse,
            ConfigPrinters.ToString,
            "Percentage of user's current currency which will be deducted every 24h",
            min: 0,
            max: 1, strict: true);

        AddNumberProp("decay.maxdecay",
            static gs => gs.Decay.MaxDecay,
            static (gs, v) => gs.Decay.MaxDecay = v,
            int.TryParse,
            ConfigPrinters.ToString,
            "Maximum amount of user's currency that can decay at each interval",
            min: 0,
            max: null, strict: true);

        AddNumberProp("decay.threshold",
            static gs => gs.Decay.MinThreshold,
            static (gs, v) => gs.Decay.MinThreshold = v,
            int.TryParse,
            ConfigPrinters.ToString,
            "Only users who have more than this amount will have their currency decay",
            min: 0,
            max: null, strict: true);

        AddParsedProp("timely.prot",
            static gs => gs.Timely.ProtType,
            static (gs, v) => gs.Timely.ProtType = v,
            ConfigParsers.InsensitiveEnum,
            ConfigPrinters.ToString,
            "How will timely be protected?");

        Migrate();
    }

    public void Migrate()
    {        
        if (Data.Version < 13)
        {
            ModifyConfig(c =>
            {
                c.Version = 13;
                c.VotePlatforms = [];
            });
        }
    }
}