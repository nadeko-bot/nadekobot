using NadekoBot.Common.Configs;

namespace NadekoBot.Modules.Games;

public sealed class FishConfigService : ConfigServiceBase<FishConfig>
{
    private static string FILE_PATH = "data/fish.yml";
    private static readonly TypedKey<FishConfig> _changeKey = new("config.fish.updated");

    public override string Name
        => "fishing";

    public FishConfigService(
        IConfigSeria serializer,
        IPubSub pubSub)
        : base(FILE_PATH, serializer, pubSub, _changeKey)
    {
        AddParsedProp("captcha",
            static conf => conf.RequireCaptcha,
            static (conf, v) => conf.RequireCaptcha = v,
            bool.TryParse,
            ConfigPrinters.ToString);

        AddParsedProp("chance.nothing",
            static conf => conf.Chance.Nothing,
            static (conf, v) => conf.Chance.Nothing = v,
            int.TryParse,
            ConfigPrinters.ToString);

        AddParsedProp("chance.fish",
            static conf => conf.Chance.Fish,
            static (conf, v) => conf.Chance.Fish = v,
            int.TryParse,
            ConfigPrinters.ToString);

        AddParsedProp("chance.trash",
            static conf => conf.Chance.Trash,
            static (conf, v) => conf.Chance.Trash = v,
            int.TryParse,
            ConfigPrinters.ToString);

        AddParsedProp("chance.currency",
            static conf => conf.Chance.Currency,
            static (conf, v) => conf.Chance.Currency = v,
            int.TryParse,
            ConfigPrinters.ToString);

        AddParsedProp("currency.min",
            static conf => conf.CurrencyMin,
            static (conf, v) => conf.CurrencyMin = v,
            long.TryParse,
            ConfigPrinters.ToString,
            "Min currency awarded when fishing out currency.");

        AddParsedProp("currency.max",
            static conf => conf.CurrencyMax,
            static (conf, v) => conf.CurrencyMax = v,
            long.TryParse,
            ConfigPrinters.ToString,
            "Max currency awarded when fishing out currency.");

        Migrate();
    }

    private void Migrate()
    {
        if (Data.Version < 11)
        {
            ModifyConfig(c =>
            {
                c.Version = 11;
                if (c.Items is { Count: > 0 })
                    return;
                c.Items =
                [
                    new FishItem
                    {
                        Id = 1,
                        ItemType = FishItemType.Pole,
                        Name = "Wooden Rod",
                        Description = "Better than catching it with bare hands.",
                        Price = 1000,
                        FishMultiplier = 1.2
                    },
                    new FishItem
                    {
                        Id = 11,
                        ItemType = FishItemType.Pole,
                        Name = "Magnet on a Stick",
                        Description = "Attracts all trash, not just metal.",
                        Price = 3000,
                        FishMultiplier = 0.9,
                        TrashMultiplier = 2
                    },
                    new FishItem
                    {
                        Id = 21,
                        ItemType = FishItemType.Bait,
                        Name = "Corn",
                        Description = "Just some cooked corn.",
                        Price = 100,
                        Uses = 100,
                        RareMultiplier = 1.1
                    },
                    new FishItem
                    {
                        Id = 31,
                        ItemType = FishItemType.Potion,
                        Name = "A Cup of Tea",
                        Description = "Helps you focus.",
                        Price = 12000,
                        DurationMinutes = 30,
                        MaxStarMultiplier = 1.1,
                        FishingSpeedMultiplier = 1.01
                    },
                    new FishItem
                    {
                        Id = 41,
                        ItemType = FishItemType.Boat,
                        Name = "Canoe",
                        Description = "Lets you fish a little faster.",
                        Price = 3000,
                        FishingSpeedMultiplier = 1.201,
                        MaxStarMultiplier = 1.1
                    }
                ];
            });
        }

        if (Data.Version < 12)
        {
            ModifyConfig(c =>
            {
                c.Version = 12;
                c.Chance.Currency = 1;
                c.CurrencyMin = 1;
                c.CurrencyMax = 1;
            });
        }
    }
}