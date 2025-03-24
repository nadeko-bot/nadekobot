#nullable disable
using NadekoBot.Common.Configs;
using NadekoBot.Modules.Gambling.Common;

namespace NadekoBot.Modules.Gambling.Services;

public sealed class GamblingConfigService : ConfigServiceBase<GamblingConfig>
{
    private const string FILE_PATH = "data/gambling.yml";
    private static readonly TypedKey<GamblingConfig> _changeKey = new("config.gambling.updated");

    public override string Name
        => "gambling";

    public GamblingConfigService(IConfigSeria serializer, IPubSub pubSub)
        : base(FILE_PATH, serializer, pubSub, _changeKey)
    {
        AddParsedProp("currency.name",
            gs => gs.Currency.Name,
            ConfigParsers.String,
            ConfigPrinters.ToString);

        AddParsedProp("currency.sign",
            gs => gs.Currency.Sign,
            ConfigParsers.String,
            ConfigPrinters.ToString);

        AddParsedProp("minbet",
            gs => gs.MinBet,
            int.TryParse,
            ConfigPrinters.ToString,
            val => val >= 0);

        AddParsedProp("maxbet",
            gs => gs.MaxBet,
            int.TryParse,
            ConfigPrinters.ToString,
            val => val >= 0);

        AddParsedProp("gen.min",
            gs => gs.Generation.MinAmount,
            int.TryParse,
            ConfigPrinters.ToString,
            val => val >= 1);

        AddParsedProp("gen.max",
            gs => gs.Generation.MaxAmount,
            int.TryParse,
            ConfigPrinters.ToString,
            val => val >= 1);

        AddParsedProp("gen.cd",
            gs => gs.Generation.GenCooldown,
            int.TryParse,
            ConfigPrinters.ToString,
            val => val > 0);

        AddParsedProp("gen.chance",
            gs => gs.Generation.Chance,
            decimal.TryParse,
            ConfigPrinters.ToString,
            val => val is >= 0 and <= 1);

        AddParsedProp("gen.has_pw",
            gs => gs.Generation.HasPassword,
            bool.TryParse,
            ConfigPrinters.ToString);

        AddParsedProp("bf.multi",
            gs => gs.BetFlip.Multiplier,
            decimal.TryParse,
            ConfigPrinters.ToString,
            val => val >= 1);

        AddParsedProp("waifu.min_price",
            gs => gs.Waifu.MinPrice,
            long.TryParse,
            ConfigPrinters.ToString,
            val => val >= 0);

        AddParsedProp("waifu.multi.reset",
            gs => gs.Waifu.Multipliers.WaifuReset,
            int.TryParse,
            ConfigPrinters.ToString,
            val => val >= 0);

        AddParsedProp("waifu.multi.crush_claim",
            gs => gs.Waifu.Multipliers.CrushClaim,
            decimal.TryParse,
            ConfigPrinters.ToString,
            val => val >= 0);

        AddParsedProp("waifu.multi.normal_claim",
            gs => gs.Waifu.Multipliers.NormalClaim,
            decimal.TryParse,
            ConfigPrinters.ToString,
            val => val > 0);

        AddParsedProp("waifu.multi.divorce_value",
            gs => gs.Waifu.Multipliers.DivorceNewValue,
            decimal.TryParse,
            ConfigPrinters.ToString,
            val => val > 0);

        AddParsedProp("waifu.multi.all_gifts",
            gs => gs.Waifu.Multipliers.AllGiftPrices,
            decimal.TryParse,
            ConfigPrinters.ToString,
            val => val > 0);

        AddParsedProp("waifu.multi.gift_effect",
            gs => gs.Waifu.Multipliers.GiftEffect,
            decimal.TryParse,
            ConfigPrinters.ToString,
            val => val >= 0);

        AddParsedProp("waifu.multi.negative_gift_effect",
            gs => gs.Waifu.Multipliers.NegativeGiftEffect,
            decimal.TryParse,
            ConfigPrinters.ToString,
            val => val >= 0);

        AddParsedProp("decay.percent",
            gs => gs.Decay.Percent,
            decimal.TryParse,
            ConfigPrinters.ToString,
            val => val is >= 0 and <= 1);

        AddParsedProp("decay.maxdecay",
            gs => gs.Decay.MaxDecay,
            int.TryParse,
            ConfigPrinters.ToString,
            val => val >= 0);

        AddParsedProp("decay.threshold",
            gs => gs.Decay.MinThreshold,
            int.TryParse,
            ConfigPrinters.ToString,
            val => val >= 0);

        AddParsedProp("timely.prot",
            gs => gs.Timely.ProtType,
            ConfigParsers.InsensitiveEnum,
           ConfigPrinters.ToString);

        Migrate();
    }

    public void Migrate()
    {        
        if (data.Version < 13)
        {
            ModifyConfig(c =>
            {
                c.Version = 13;
                c.VotePlatforms = [];
            });
        }
    }
}