using NadekoBot.Common.Configs;

namespace NadekoBot.Modules.Waifus.Waifu;

public sealed class WaifuConfigService : ConfigServiceBase<WaifuConfig>
{
    private const string FILE_PATH = "data/waifu.yml";
    private static readonly TypedKey<WaifuConfig> _changeKey = new("config.waifu.updated");

    public override string Name
        => "waifu";

    public WaifuConfigService(IConfigSeria serializer, IPubSub pubSub)
        : this(FILE_PATH, serializer, pubSub) { }

    internal WaifuConfigService(string filePath, IConfigSeria serializer, IPubSub pubSub)
        : base(filePath, serializer, pubSub, _changeKey)
    {
        AddParsedProp("minprice",
            static c => c.MinPrice,
            static (c, v) => c.MinPrice = v,
            long.TryParse,
            ConfigPrinters.ToString,
            "Minimum price a waifu can have. Default 1000",
            static val => val >= 0);

        AddParsedProp("optincost",
            static c => c.OptInCost,
            static (c, v) => c.OptInCost = v,
            long.TryParse,
            ConfigPrinters.ToString,
            "Cost to opt into the waifu system. Default 10000",
            static val => val >= 0);

        AddParsedProp("decay",
            static c => c.ManagerlessDecayPercent,
            static (c, v) => c.ManagerlessDecayPercent = v,
            int.TryParse,
            ConfigPrinters.ToString,
            "Price decay percentage per cycle for waifus without a manager (0-100). Default 10",
            static val => val is >= 0 and <= 100);

        Migrate();
    }

    private void Migrate()
    {
        if (Data.Version < 1)
        {
            ModifyConfig(c =>
            {
                c.Version = 1;
            });

            BackupOldWaifuConfig();
        }

        if (Data.Version < 2)
        {
            ModifyConfig(c =>
            {
                c.Version = 2;
                c.CycleHours = 84.0;
                c.BaseReturnRate = 0.17;
                c.DefaultReturnsCap = 1_000_000;
                c.BuyWindowHours = 18;
                c.BaseMoodIncrease = 50;
                c.MaxDailyActions = 2;
                c.MaxGiftCount = 100;
                c.ManagerBuyPremium = 0.15;
                c.ManagerCutPercent = 0.15;
            });
        }
        if (Data.Version < 3)
        {
            ModifyConfig(c =>
            {
                c.Version = 3;
                c.CycleHours = 24.0;
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
                c.SurplusWaifuShare = 0.50;
            });
        }

        if (Data.Version < 7)
        {
            ModifyConfig(c =>
            {
                c.Version = 7;
                c.BaseFoodIncrease = 50;
            });
        }
    }

    /// <summary>
    /// Backs up the old waifu config section from gambling.yml to waifuconfig.old.yml.
    /// </summary>
    private static void BackupOldWaifuConfig()
    {
        const string gamblingPath = "data/gambling.yml";
        const string backupPath = "data/waifuconfig.old.yml";

        if (!File.Exists(gamblingPath) || File.Exists(backupPath))
            return;

        try
        {
            var lines = File.ReadAllLines(gamblingPath);
            var waifuLines = new List<string>();
            var capturing = false;
            var baseIndent = -1;

            foreach (var line in lines)
            {
                if (!capturing)
                {
                    if (line.TrimStart().StartsWith("waifu:"))
                    {
                        capturing = true;
                        baseIndent = line.Length - line.TrimStart().Length;
                        waifuLines.Add(line);
                    }
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    waifuLines.Add(line);
                    continue;
                }

                var indent = line.Length - line.TrimStart().Length;
                if (indent <= baseIndent && !line.TrimStart().StartsWith("#"))
                    break;

                waifuLines.Add(line);
            }

            if (waifuLines.Count > 1)
                File.WriteAllLines(backupPath, waifuLines);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to backup old waifu config");
        }
    }
}
